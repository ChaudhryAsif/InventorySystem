using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Models;
public static class SaleGroupSelection
{
    public static async Task<int?> GroupIdAsync(ApplicationDbContext db,string? customer)
    {
        string? value=customer;
        if(int.TryParse(customer,out var partyId))value=await db.Parties.Where(p=>p.PartyId==partyId).Select(p=>p.PartyCode).SingleOrDefaultAsync();
        if(value?.StartsWith("group:",StringComparison.Ordinal)==true)value=value[6..];
        else if(value?.StartsWith("FG-GROUP:",StringComparison.Ordinal)==true)value=value[9..];
        else if(customer?.StartsWith("group:",StringComparison.Ordinal)==true)throw new InvalidOperationException("Select a valid Finished Goods group.");
        else return null;
        if(!int.TryParse(value,out var id)||!await db.ItemCategory.AnyAsync(g=>g.CategoryId==id&&g.GroupType=="FinishedGoods"&&g.IsActive==true))throw new InvalidOperationException("Select an active Finished Goods group.");
        return id;
    }
    public static async Task ValidateItemsAsync(ApplicationDbContext db,SaleInvoiceViewModel model)
    {
        var group=await GroupIdAsync(db,model.CustomerID);
        if(group.HasValue){var ids=model.Items.Select(i=>i.ItemId).ToArray();
            if(ids.Any(i=>i==null)||await db.Items.CountAsync(i=>ids.Contains(i.ItemID)&&i.CategoryID==group&&i.ItemType=="FinishedGoods"&&i.IsActive==true)!=ids.Distinct().Count())throw new InvalidOperationException("Choose only Finished Goods items belonging to the selected customer group.");}
    }
}
