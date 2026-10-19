/* Client carton costing. Pure calculations; no DOM access. */
(function (root) {
    'use strict';
    const round = (x, digits = 2) => Math.round((x + Number.EPSILON) * 10 ** digits) / 10 ** digits;
    function calculate(m, c) {
        const unit = m.sizeUnit === 'in' ? 25.4 : 1;
        const width = ((m.width || 0) + (m.height || 0) - (m.autoFlapGap || 0)) * unit / 25.4;
        const length = 2 * ((m.length || 0) + (m.width || 0)) * unit / 25.4;
        const flap = (m.flap || 0) * unit / 25.4;
        const aw = width + c.widthAllowanceIn, al = length + flap + c.lengthAllowanceIn;
        const area = m.length > 0 && m.width > 0 && m.height > 0 ? Math.max(0, aw * al) : 0;
        const plies = m.plies.map((p, i) => {
            const kg = area / 1550 * (p.gsm || 0) / 1000 * (c.flutedLayers[i] ? c.fluteFactor : 1);
            return {kg, cost:kg * (p.rate || 0)};
        });
        const weight = plies.reduce((n, p) => n + p.kg, 0), paper = plies.reduce((n, p) => n + p.cost, 0);
        const glueKg = area / 1550 * .025 * c.glueApplications, silicateKg = area / 1550 * .025 * c.silicateApplications;
        const glueCost = glueKg * m.glueRate + silicateKg * m.silicateRate;
        const glueLamKg = m.hasLamination ? area / 1550 * .0025 * c.glueLaminationApplications : 0;
        const chemicalKg = m.hasLamination ? area / 1550 * .0025 * c.laminationChemicalApplications : 0;
        const glueLam = glueLamKg * c.glueLaminationRate, chemical = chemicalKg * c.laminationChemicalRate;
        const printing = area / 1000 * m.printingColors * c.printingRate;
        const lamination = m.hasLamination ? round(area / 144 * c.laminationRate) : 0;
        const labour = weight * m.labourRate, energy = weight * m.energyRate, freight = weight * m.freightRate;
        const binding = m.bindingType === 'None' ? 0 : m.bindingType === 'Pins' ? m.bindingPins * c.pinRate : m.bindingCost;
        const wood = weight * c.woodRate, starch = weight * c.starchRate;
        const waste = weight * m.wastePct, admin = weight * m.adminExpPct, selling = weight * m.sellingDistPct;
        const repair = weight * m.repairMaintPct, spares = weight * m.storeSparesPct, manufacturing = weight * m.mfgCostPct;
        const operating = glueCost + glueLam + chemical + labour + energy + freight + binding + wood + starch + waste + admin + selling + repair + spares + manufacturing;
        const basis = paper + printing + lamination + m.dickelCost + m.otherMaterialsCost + operating;
        const commissionA = basis * (m.commPersonAPct || 0) / 100, commissionB = basis * (m.commPersonBPct || 0) / 100;
        const additionalTax = basis * (m.taxPct || 0) / 100;
        const profit = basis * m.profitPct / 100, gst = (basis + profit) * m.gstPct / 100, withholding = operating * m.whTaxPct / 100;
        const finalWOGST = basis + commissionA + commissionB + profit, unitPrice = finalWOGST / (m.moq > 0 ? m.moq : 1);
        return {plies, paperWeightKg:weight, area, sheetWidthIn:width, sheetLengthIn:length, withFlap:flap,
            adjWidthIn:aw, adjLength1In:al, adjLength2In:al/2, adjWidthMm:aw*25.4, adjLengthMm:al*25.4, deckelIn:width*c.deckelUp+1,
            glueKgPerBox:glueKg, silicateKgPerBox:silicateKg, glueSilicateKg:glueKg+silicateKg, glueSilicateCost:glueCost,
            glueLaminationKg:glueLamKg, laminationChemicalKg:chemicalKg, glueLaminationCost:glueLam, laminationChemicalCost:chemical,
            printingCost:printing, laminationCost:lamination, paperCost:paper, labourCost:labour, energyCost:energy, freightCost:freight,
            bindingCost:binding, woodCost:wood, starchCost:starch, wasteCost:waste, adminExpCost:admin, sellingDistCost:selling,
            repairMaintCost:repair, storeSparesCost:spares, mfgCostValue:manufacturing, operatingCost:operating,
            subTotal:basis-waste-admin-selling-repair-spares-manufacturing, manufacturingTotal:basis,
            commPersonACost:commissionA, commPersonBCost:commissionB, additionalTaxAmount:additionalTax,
            profitAmount:profit, gstAmount:gst, withholdingAmount:withholding, finalCostWOGST:finalWOGST,
            pricePerUnit:unitPrice, finalRateWithGST:unitPrice+withholding+gst+additionalTax};
    }
    root.ClientCartonCosting = {calculate, round};
    if (typeof module !== 'undefined') module.exports = root.ClientCartonCosting;
})(typeof window === 'undefined' ? globalThis : window);
