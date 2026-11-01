using InventorySystem.Authorization;
using InventorySystem.Core.Models;
using InventorySystem.Models;
namespace InventorySystem.Tests;
public class InventoryEditLockTests
{
    private static bool Blocked(string controller,string action,bool write,params object?[] args)=>OperationFilter.InventoryEditBlocked(controller,action,write,args);
    [Theory][InlineData("Sale")][InlineData("SaleReturn")][InlineData("Purchase")][InlineData("PurchaseReturn")][InlineData("Production")]
    public void UpdateActionsAreLocked(string controller)=>Assert.True(Blocked(controller,"Update",true));
    [Fact] public void ProductionEditPageIsLocked(){Assert.True(Blocked("Production","Edit",false));Assert.False(Blocked("Production","Index",false));}
    [Fact] public void CostSheetsAreEditableByUsersWithThePermission()
    {
        Assert.False(Blocked("CostSheet","Edit",false));
        Assert.False(Blocked("CostSheet","Save",true,new CostSheetViewModel { CostSheetId=4 }));
        Assert.True(Blocked("CostSheet","Update",true));
    }
    [Fact] public void SavingAnExistingRecordIsLockedButCreatingIsNot()
    {
        Assert.True(Blocked("Category","Save",true,new ItemCategoryViewModel { CategoryId=4,CategoryName="x" }));
        Assert.False(Blocked("Category","Save",true,new ItemCategoryViewModel { CategoryId=0,CategoryName="x" }));
        Assert.True(Blocked("GatePass","Save",true,new GatePass { Id=7 }));
        Assert.False(Blocked("GatePass","Save",true,new GatePass { Id=0 }));
    }
    [Fact] public void NewProductionOrderCarryingACostSheetIdIsNotLocked()=>Assert.False(Blocked("Production","Save",true,new ProductionOrderViewModel { CostSheetId=1 }));
    [Theory][InlineData("Production","Complete")][InlineData("Production","SaveConsumption")][InlineData("Production","Cancel")][InlineData("CostSheet","UpdateFinishedItemLink")][InlineData("CostSheet","MarkFinal")][InlineData("Sale","Delete")][InlineData("GatePass","Cancel")]
    public void WorkflowAndDeleteActionsStayOpen(string controller,string action)=>Assert.False(Blocked(controller,action,true,new GatePass { Id=7 }));
    [Theory][InlineData("Accounts")][InlineData("Parties")][InlineData("Admin")]
    public void AccountsAndOtherAreasAreNotAffected(string controller){Assert.False(Blocked(controller,"Update",true));Assert.False(Blocked(controller,"Save",true,new Party { PartyId=3 }));Assert.False(Blocked(controller,"Edit",false));}
}
