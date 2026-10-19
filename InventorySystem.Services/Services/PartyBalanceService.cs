using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Core.Services;

// Invoice party movements plus manual receivable/payable movements, without counting invoice GL twice.
public sealed class PartyBalanceService(ApplicationDbContext db)
{
    public static PartyBalancePosition Position(string? partyType, decimal signedBalance)
    {
        var supplier = partyType == "supplier";
        var receivable = supplier ? signedBalance < 0 : signedBalance > 0;
        var refund = signedBalance < 0 && partyType is "supplier" or "customer";
        var control = supplier || partyType == "both" && signedBalance < 0 ? "2201" : "1301";
        var description = signedBalance == 0 ? "Cleared" : refund
            ? supplier ? "Supplier refund due" : "Customer refund due"
            : receivable ? "Customer payment due" : "Supplier payment due";
        return new(Math.Abs(signedBalance), receivable ? "Receivable" : "Payable", control, refund, description);
    }

    public async Task<List<PartyStatementRow>> StatementAsync(int partyId, DateTime? from = null, DateTime? to = null)
    {
        var party = await db.Parties.SingleOrDefaultAsync(p => p.PartyId == partyId);
        if (party == null) return [];
        var end = !to.HasValue || to.Value.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : to.Value.Date.AddDays(1);
        var rows = await db.AccountLedger.Where(l => l.PartyId == partyId && l.EntryDate < end)
            .OrderBy(l => l.EntryDate).ThenBy(l => l.LedgerId)
            .Select(l => new PartyStatementRow { Date = l.EntryDate, VoucherNo = l.ReferenceId.ToString() ?? "",
                TransactionType = l.TransactionType, Description = l.Description ?? "", Debit = l.Debit, Credit = l.Credit }).ToListAsync();
        var auto = await db.SaleInvoice.Where(i => i.GLVoucherId != null).Select(i => i.GLVoucherId!.Value)
            .Concat(db.PurchaseInvoice.Where(i => i.GLVoucherId != null).Select(i => i.GLVoucherId!.Value))
            .Concat(db.SaleReturn.Where(i => i.GLVoucherId != null).Select(i => i.GLVoucherId!.Value))
            .Concat(db.PurchaseReturn.Where(i => i.GLVoucherId != null).Select(i => i.GLVoucherId!.Value)).ToListAsync();
        var controls = await db.AccountHeads.Where(a => a.AccountCode == "1301" || a.AccountCode == "2201").Select(a => a.AccountHeadId).ToListAsync();
        var manual = await db.GeneralLedger.Where(g => !g.IsVoid && g.PartyId == partyId && g.VoucherDate < end && controls.Contains(g.AccountHeadId) &&
                (!g.VoucherId.HasValue || !auto.Contains(g.VoucherId.Value)))
            .OrderBy(g => g.VoucherDate).ThenBy(g => g.GLId)
            .Select(g => new PartyStatementRow { Date = g.VoucherDate, VoucherNo = g.VoucherNo,
                TransactionType = g.VoucherType, Description = g.Narration ?? "", Debit = g.Debit, Credit = g.Credit }).ToListAsync();
        rows.AddRange(manual);
        rows = rows.OrderBy(r => r.Date).ToList();
        var supplier = party.PartyType == "supplier";
        var opening = party.OpeningBalance ?? 0;
        var openingDebit = supplier ? Math.Max(0, -opening) : Math.Max(0, opening);
        var openingCredit = supplier ? Math.Max(0, opening) : Math.Max(0, -opening);
        if (from.HasValue)
        {
            var previous = rows.Where(r => r.Date < from.Value.Date).ToList();
            openingDebit += previous.Sum(r => r.Debit); openingCredit += previous.Sum(r => r.Credit);
            rows = rows.Where(r => r.Date >= from.Value.Date).ToList();
        }
        var openingNet = openingDebit - openingCredit;
        if (openingNet != 0)
            rows.Insert(0, new PartyStatementRow { Date = from?.Date ?? new DateTime(2000,1,1), VoucherNo = "OPENING",
                TransactionType = "Opening", Description = from.HasValue ? "Opening balance brought forward" : "Party opening balance",
                Debit = Math.Max(0,openingNet), Credit = Math.Max(0,-openingNet) });
        decimal running = 0;
        foreach (var row in rows) { running += supplier ? row.Credit - row.Debit : row.Debit - row.Credit; row.Balance = running; }
        return rows;
    }
}

public readonly record struct PartyBalancePosition(decimal Amount, string BalanceType, string ControlCode, bool IsRefund, string Description);
