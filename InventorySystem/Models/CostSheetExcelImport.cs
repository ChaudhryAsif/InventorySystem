using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using InventorySystem.Core.Models;

namespace InventorySystem.Models;

public sealed class CostSheetImportPreview
{
    public string Worksheet { get; set; } = "";
    public CostSheetViewModel Data { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}

// Read only the agreed carton layout. Never evaluate workbook formulas or follow links.
public static class CostSheetExcelImport
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static CostSheetImportPreview Read(Stream input)
    {
        try { return ReadWorkbook(input); }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or ArgumentException or IOException or OverflowException)
        { throw new InvalidOperationException("The Excel file cannot be read. Upload a valid .xlsx file using the sample cost-sheet format."); }
    }

    private static CostSheetImportPreview ReadWorkbook(Stream input)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 32 * 1024 * 1024 || zip.Entries.Any(e => e.Length > 4 * 1024 * 1024))
            throw new InvalidOperationException("This workbook is too large. Upload one cost sheet using the sample format.");
        if (zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Macro-enabled workbooks are not supported. Save a copy as .xlsx first.");
        XDocument Xml(string path)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidOperationException("The workbook structure is incomplete.");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
            return XDocument.Load(reader);
        }
        var strings = zip.GetEntry("xl/sharedStrings.xml") == null ? [] : Xml("xl/sharedStrings.xml").Descendants(S + "si").Select(x => string.Concat(x.Descendants(S + "t").Select(t => t.Value))).ToArray();
        var workbook = Xml("xl/workbook.xml");
        if ((string?)workbook.Root?.Element(S + "workbookPr")?.Attribute("date1904") is "1" or "true")
            throw new InvalidOperationException("This workbook uses a different Excel date system. Save it with the sample's date system before importing.");
        var relationships = Xml("xl/_rels/workbook.xml.rels");
        var candidates = new List<(string Name, Dictionary<string, Cell> Cells)>();
        foreach (var sheet in workbook.Descendants(S + "sheet"))
        {
            var rel = relationships.Root?.Elements().SingleOrDefault(e => (string?)e.Attribute("Id") == (string?)sheet.Attribute(R + "id"));
            if (rel == null || (string?)rel.Attribute("TargetMode") == "External") continue;
            var target = (string?)rel.Attribute("Target") ?? "";
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (!Regex.IsMatch(path, @"^xl/worksheets/[A-Za-z0-9_.-]+\.xml$")) continue;
            var cells = Xml(path).Descendants(S + "c").ToDictionary(x => (string)x.Attribute("r")!, x => {
                var value = x.Element(S + "v")?.Value ?? "";
                var type = (string?)x.Attribute("t");
                if (type == "s") {
                    if (!int.TryParse(value, out var index) || index < 0 || index >= strings.Length) throw new InvalidOperationException("Invalid Excel text data.");
                    value = strings[index];
                }
                if (type == "inlineStr") value = string.Concat(x.Descendants(S + "t").Select(t => t.Value));
                return new Cell(value.Trim(), x.Element(S + "f")?.Value, type == "e");
            });
            string At(string address) => cells.GetValueOrDefault(address)?.Text ?? "";
            if (At("C3") == "Ref#" && At("C7") == "Customer" && At("C9") == "Item#" && At("P18") == "Ply" && At("AB18") == "GSM")
                candidates.Add(((string?)sheet.Attribute("name") ?? "Cost sheet", cells));
        }
        if (candidates.Count != 1) throw new InvalidOperationException(candidates.Count == 0
            ? "This Excel file doesn't match the required cost sheet format. Please use the downloadable Excel template."
            : "This workbook contains more than one cost sheet. Upload a workbook containing only the sheet you want to import.");
        var selected = candidates[0]; var c = selected.Cells;
        string Formula(string address) => (c.GetValueOrDefault(address)?.Formula ?? "").Replace(" ", "").Replace("$", "").TrimStart('=');
        var latest = Formula("R63") == "R61+K45" && Formula("P54") == "K49+AD63+Z45+K52";
        if (!latest && !(Formula("R63") == "R61+K45+K47" && Formula("P54") == "K49+AD63"))
            throw new InvalidOperationException("The final-price formulas do not match a supported client cost sheet.");
        string Text(string a) {
            var cell = c.GetValueOrDefault(a);
            if (cell?.Error == true || (cell?.Formula != null && cell.Text.Length == 0)) throw new InvalidOperationException($"Cell {a} has no valid value. Open and save the file in Excel, then upload it again.");
            return cell?.Text ?? "";
        }
        decimal Number(string a, bool required = true) {
            var text = Text(a);
            // Empty numeric inputs are zero; required counts, dimensions, date and GSM are validated below.
            if (text == "") return 0;
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 1000000)
                throw new InvalidOperationException($"Enter a valid nonnegative number in Excel cell {a}.");
            return n;
        }
        int Integer(string a, int min, int max) {
            var n = Number(a); if (n != decimal.Truncate(n) || n < min || n > max) throw new InvalidOperationException($"Cell {a} must be a whole number from {min} to {max}.");
            return (int)n;
        }
        decimal FormulaConstant(string address, string pattern) {
            var formula = c.GetValueOrDefault(address)?.Formula ?? "";
            var match = Regex.Match(formula.Replace(" ", "").Replace("$", ""), pattern, RegexOptions.IgnoreCase);
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 1000000)
                throw new InvalidOperationException($"The formula in {address} differs from the sample format. Review that formula before importing.");
            return n;
        }
        if (Text("E14") != "Regular Slotted Carton (RSC)" || Text("E16") != "External" || !new[] { "Milimeters", "Millimeters", "mm" }.Contains(Text("I16")))
            throw new InvalidOperationException("This import supports the sample's external RSC carton dimensions in millimeters.");
        if (Text("AF16") is not ("Yes" or "No")) throw new InvalidOperationException("Lamination must be Yes or No in cell AF16.");
        var count = Integer("R18", 1, 5);
        if (latest) {
            var populated = Enumerable.Range(0,5).Where(i => Text("T"+(20+2*i)) != "" || Number("AB"+(20+2*i),false) != 0 || Number("AD"+(20+2*i),false) != 0).ToList();
            if (populated.Count == 0 || populated[^1] >= count || !populated.SequenceEqual(Enumerable.Range(0,populated.Count)))
                throw new InvalidOperationException("Fill consecutive paper layers from the top and include them in the ply count.");
            count = populated.Count;
        }
        var options = new ClientCostingOptions {
            Version = latest ? 2 : 1,
            WidthAllowanceIn = FormulaConstant("G30", @"^=?G26\+([0-9.]+)$"),
            LengthAllowanceIn = FormulaConstant("K30", @"^=?K26\+K28\+([0-9.]+)$"),
            DeckelUp = Integer("R13", 1, 100),
            LaminationRate = Number("AF14"),
            PinRate = FormulaConstant("AJ43", @"^=?([0-9.]+)\*AF43$"),
            GlueApplications = Number("T32"), SilicateApplications = Number("T33"),
            GlueLaminationApplications = Number("T31"), LaminationChemicalApplications = Number("T34"),
            GlueLaminationRate = FormulaConstant("Z31", @"^=?SUM\(K34/1550\)\*([0-9.]+)$"),
            LaminationChemicalRate = FormulaConstant("Z34", @"^=?SUM\(K34/1550\)\*([0-9.]+)$"),
            WoodRate = Number("AF45"), StarchRate = Number("AF47")
        };
        // The sample has no separate printing-rate input. Require its known formula rather than guessing a rate.
        var printingFormula = c.GetValueOrDefault("AF7")?.Formula?.Replace(" ", "").Replace("$", "");
        if (printingFormula is not ("SUM((K34/1000)*AB7)" or "=SUM((K34/1000)*AB7)"))
            throw new InvalidOperationException("The printing formula in AF7 differs from the sample. Enter this sheet manually or use the sample formula.");
        var m = new CostSheetViewModel {
            ClientCosting = options, Status = "Draft", ItemName = Text("G9"), BoxStyle = Text("E14"), SizeType = "External", SizeUnit = "mm",
            Length = Number("E20"), Width = Number("G20"), Height = Number("I20"), Flap = Number("K20"), AutoFlapGap = Number("G22"),
            PrintingType = Text("AB5"), PrintingColors = Integer("AB7", 0, 100), HasLamination = Text("AF16") == "Yes",
            GlueRate = FormulaConstant("Z32", @"^=?SUM\(K34/1550\)\*([0-9.]+)$"), SilicateRate = FormulaConstant("Z33", @"^=?SUM\(K34/1550\)\*([0-9.]+)$"),
            LabourRate = Number("AF39"), EnergyRate = Number("AF41"), BindingType = latest && Text("Z43") == "None" ? "None" : "Pins", BindingPins = Integer("AF43", 0, 1000000),
            FreightRate = latest ? Number("Z45",false) : 0,
            WastePct = Number("AF49"), AdminExpPct = Number("AF51"), SellingDistPct = Number("AF54"), RepairMaintPct = Number("AF57"),
            StoreSparesPct = Number("AF59"), MfgCostPct = Number("AF61"), ProfitPct = Number("G49"), GSTPercentage = Number("G47"),
            WHTaxPct = Number("G45", false), CommPersonAPct = Number("G39", false), CommPersonBPct = Number("G41", false), TaxPct = Number("T39", false),
            CommPersonA = Text("P39"), CommPersonB = Text("P41"), MOQ = Integer("K61", 1, 1000000),
            Remarks = "Imported Excel reference. Customer: " + Text("G7") + (Text("E3") == "" ? "" : "; Ref: " + Text("E3"))
        };
        if (string.IsNullOrWhiteSpace(m.ItemName) || m.ItemName.Length > 300) throw new InvalidOperationException("Enter an item name of up to 300 characters in G9.");
        if (!new[] { "", "Flexo", "Offset", "Digital", "Pantone" }.Contains(m.PrintingType)) throw new InvalidOperationException("Select a supported printing type in AB5.");
        var date = Number("I3"); if (date is < 1 or > 2958465) throw new InvalidOperationException("Enter a valid Excel date in I3.");
        m.SheetDate = DateTime.FromOADate((double)date).Date;
        decimal? fluteFactor = null;
        for (int i = 0; i < count; i++) {
            var row = 20 + 2 * i; var fluted = Text("P" + row).Contains("(F)", StringComparison.OrdinalIgnoreCase);
            if (i == 0 ? Text("P20") != "Top Layer" : !new[] { $"Layer {i + 1} (F)", $"Layer {i + 1} (P)" }.Contains(Text("P" + row)))
                throw new InvalidOperationException($"The layer label in P{row} does not match the sample format.");
            if (fluted) {
                var factor = FormulaConstant("AH" + row, $@"^=?\(K34/1550\)\*\(AB{row}/1000\)\*([0-9.]+)$");
                if (!latest && fluteFactor != null && fluteFactor != factor) throw new InvalidOperationException("All fluted layers must use the same flute factor.");
                fluteFactor ??= factor; options.FluteFactor = fluteFactor.Value;
                options.LayerFluteFactors.Add(factor);
            }
            else options.LayerFluteFactors.Add(1);
            options.FlutedLayers.Add(fluted);
            var spec = Text("T" + row); if (spec.Length == 0 || spec.Length > 100) throw new InvalidOperationException($"Enter a paper specification in T{row} (up to 100 characters).");
            m.Plies.Add(new() { SrNo = i + 1, Specification = spec, GSM = Number("AB" + row), Rate = Number("AD" + row) });
        }
        for (int i = count; i < 5; i++) {
            var row = 20 + 2 * i;
            if (Number("AB" + row, false) > 0 || Number("AD" + row, false) > 0) throw new InvalidOperationException("The ply count in R18 does not include all populated paper layers. Correct the count before importing.");
        }
        if (new[] { m.ProfitPct,m.GSTPercentage,m.WHTaxPct,m.CommPersonAPct,m.CommPersonBPct,m.TaxPct }.Any(n => n > 100))
            throw new InvalidOperationException("Profit, tax and commission percentages must be between 0 and 100.");
        if (m.Remarks.Length > 500) throw new InvalidOperationException("The customer name and reference are too long. Shorten them before importing.");
        if (m.Length <= 0 || m.Width <= 0 || m.Height <= 0 || m.Plies.Any(p => p.GSM <= 0)) throw new InvalidOperationException("Carton dimensions and paper GSM must be greater than zero.");
        ClientCartonCalculation.Prepare(m); ClientCartonCalculation.Calculate(m);
        return new() { Worksheet = selected.Name, Data = m, Warnings = [
            "Review the imported values before saving. Excel totals are recalculated using the system's costing rules, including GST on cost plus profit and zero lamination costs when No is selected.",
            "Excel item and customer numbers are not software IDs. Select the finished item, paper items and any adhesive stock items before saving. No records or stock movements are created by this upload."
        ] };
    }
    private sealed record Cell(string Text, string? Formula, bool Error);
}
