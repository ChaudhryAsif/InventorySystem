using System.Text.Json;
using InventorySystem.Controllers;
using InventorySystem.Core.Models;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace InventorySystem.Tests;

public class RefundFlowTests
{
    private static ApplicationDbContext Db(string type,decimal opening=0)
    {
        var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.Database.EnsureCreated();db.Parties.Add(new Party {PartyId=900,PartyName="Refund party",PartyType=type,OpeningBalance=opening,Status="active"});
        db.ItemCategory.Add(new ItemCategory { CategoryId = 1, CategoryName = "Test category", IsActive = true });
        db.Items.Add(new Items {ItemID=900,ItemName="Goods",CategoryID=1,IsActive=true});
        db.Stock.Add(new Stock {ItemId=900,BranchId=1,Quantity=1000});db.SaveChanges();return db;
    }
    private static ControllerContext Http()
    {
        var http=new DefaultHttpContext();http.Request.Headers["X-Operation-Reason"]="Refund regression cancellation";
        return new ControllerContext {HttpContext=http};
    }
    private static AccountsController Accounts(ApplicationDbContext db)=>new(db,new AccountService(db)) {ControllerContext=Http()};
    private static PartyPaymentRequest Payment(string direction,decimal amount)=>new(){PartyId=900,Direction=direction,Amount=amount,Date=DateTime.Today};
    private static JsonElement Data(IActionResult result)=>JsonSerializer.SerializeToElement(result switch { ObjectResult o => o.Value, JsonResult j => j.Value, _ => throw new Exception("Unexpected action result") });
    private static async Task<decimal> Balance(ApplicationDbContext db)=>(await new AccountService(db).GetPartyStatementAsync(900,null,null)).LastOrDefault()?.Balance??0;

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task CancelledPartPaidPurchase_RefundCanBeReceivedAndVoided(bool gl)
    {
        using var db=Db("supplier");var accounts=new AccountService(db);
        var purchase=new PurchaseController(db,new LedgerPostingService(db,accounts),Options.Create(new AccountingOptions {PostToGeneralLedger=gl}),new StockService(db)) {ControllerContext=Http()};
        Assert.True(Data(await purchase.Save(new PurchaseInvoiceViewModel {VendorID="900",BranchID=1,PaymentMode=0,PurchaseDate=DateTime.Today,
            Items=[new(){Itemid=900,Quantity=100,PurPrice=100}]})).GetProperty("success").GetBoolean());
        Assert.IsType<OkObjectResult>(await Accounts(db).SavePayment(Payment("Payment",3000)));
        Assert.True(Data(await purchase.Delete((await db.PurchaseInvoice.SingleAsync()).PurchaseId)).GetProperty("success").GetBoolean());
        Assert.Equal(-3000,await Balance(db));
        var due=(await accounts.GetOutstandingReportAsync("supplier")).Single();Assert.Equal(3000,due.Balance);Assert.Equal("Receivable",due.BalanceType);Assert.True(due.IsRefund);
        var summary=Data(await Accounts(db).GetLedger(900)).GetProperty("summary");Assert.Equal(3000,summary.GetProperty("balance").GetDecimal());Assert.True(summary.GetProperty("isRefund").GetBoolean());
        var aging=(await accounts.GetAgingReportAsync("supplier",DateTime.Today)).Rows.Single();Assert.Equal(3000,aging.Total);Assert.Equal("Receivable",aging.BalanceType);
        Assert.IsType<BadRequestObjectResult>(await Accounts(db).SavePayment(Payment("Payment",1000)));
        var refund=Data(await Accounts(db).SavePayment(Payment("Receipt",1000)));var id=refund.GetProperty("id").GetInt32();
        Assert.Equal(-2000,await Balance(db));var payableId=(await db.AccountHeads.SingleAsync(a=>a.AccountCode=="2201")).AccountHeadId;
        Assert.Equal(1000,(await db.GeneralLedger.SingleAsync(g=>g.VoucherId==id&&g.PartyId==900&&g.AccountHeadId==payableId)).Credit);
        Assert.True((await accounts.VoidVoucherAsync(id)).success);Assert.Equal(-3000,await Balance(db));
        Assert.IsType<OkObjectResult>(await Accounts(db).SavePayment(Payment("Receipt",3000)));Assert.Equal(0,await Balance(db));
        Assert.Empty(await accounts.GetOutstandingReportAsync("all"));Assert.Empty((await accounts.GetAgingReportAsync("supplier",DateTime.Today)).Rows);
        Assert.IsType<BadRequestObjectResult>(await Accounts(db).SavePayment(Payment("Receipt",1)));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task CancelledPartPaidSale_CustomerRefundClearsReceivableControl(bool gl)
    {
        using var db=Db("customer");var accounts=new AccountService(db);
        var sale=new SaleController(db,new LedgerPostingService(db,accounts),Options.Create(new AccountingOptions {PostToGeneralLedger=gl}),new StockService(db)) {ControllerContext=Http()};
        Assert.True(Data(await sale.Save(new SaleInvoiceViewModel {CustomerID="900",BranchID=1,PaymentMode=0,SaleDate=DateTime.Today,
            Items=[new(){ItemId=900,Quantity=100,SalePrice=100}]})).GetProperty("success").GetBoolean());
        Assert.IsType<OkObjectResult>(await Accounts(db).SavePayment(Payment("Receipt",3000)));
        Assert.True(Data(await sale.Delete((await db.SaleInvoice.SingleAsync()).SaleId)).GetProperty("success").GetBoolean());
        Assert.Equal(-3000,await Balance(db));var due=(await accounts.GetOutstandingReportAsync("customer")).Single();Assert.Equal("Payable",due.BalanceType);Assert.Equal(3000,due.Balance);Assert.True(due.IsRefund);
        var aging=(await accounts.GetAgingReportAsync("customer",DateTime.Today)).Rows.Single();Assert.Equal(3000,aging.Total);Assert.Equal("Payable",aging.BalanceType);
        Assert.IsType<BadRequestObjectResult>(await Accounts(db).SavePayment(Payment("Receipt",1000)));
        var refund=Data(await Accounts(db).SavePayment(Payment("Payment",3000)));Assert.Equal(0,await Balance(db));
        var control=(await db.AccountHeads.SingleAsync(a=>a.AccountCode=="1301")).AccountHeadId;
        Assert.Equal(3000,(await db.GeneralLedger.SingleAsync(g=>g.VoucherId==refund.GetProperty("id").GetInt32()&&g.AccountHeadId==control)).Debit);
    }

    [Theory]
    [InlineData("supplier",2000,"Payment","Payable","2201")]
    [InlineData("supplier",-2000,"Receipt","Receivable","2201")]
    [InlineData("customer",2000,"Receipt","Receivable","1301")]
    [InlineData("customer",-2000,"Payment","Payable","1301")]
    [InlineData("both",2000,"Receipt","Receivable","1301")]
    [InlineData("both",-2000,"Payment","Payable","2201")]
    public async Task OpeningBalances_SettleOnlyInCorrectDirection(string type,decimal opening,string direction,string balanceType,string code)
    {
        using var db=Db(type,opening);var accounts=new AccountService(db);
        Assert.Equal(balanceType,(await accounts.GetOutstandingReportAsync(type=="both"?"all":type)).Single().BalanceType);
        Assert.IsType<BadRequestObjectResult>(await Accounts(db).SavePayment(Payment(direction=="Receipt"?"Payment":"Receipt",100)));
        Assert.IsType<BadRequestObjectResult>(await Accounts(db).SavePayment(Payment(direction,2000.01m)));
        Assert.IsType<BadRequestObjectResult>(await Accounts(db).SavePayment(Payment(direction,0.001m)));
        var id=Data(await Accounts(db).SavePayment(Payment(direction,2000))).GetProperty("id").GetInt32();
        var control=(await db.AccountHeads.SingleAsync(a=>a.AccountCode==code)).AccountHeadId;
        Assert.Contains(await db.GeneralLedger.ToListAsync(),g=>g.VoucherId==id&&g.AccountHeadId==control&&g.PartyId==900);
        Assert.Equal(0,await Balance(db));Assert.Empty(await accounts.GetOutstandingReportAsync("all"));
    }
}
