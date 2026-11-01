using InventorySystem.Core.Models;
using InventorySystem.Data;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Services.Services;

public static class FinishedGoodsCustomers
{
    public const string Prefix="FG-GROUP:";
    // Resolve only inside the production save transaction. GET lookups never create accounts.
    public static async Task<string> ResolveAsync(ApplicationDbContext db,string? selection)
    {
        if(selection?.StartsWith("group:",StringComparison.Ordinal)!=true)
        {
            var id=await EstimatedProductionService.ValidateCustomerAsync(db,selection);
            var account=await db.Parties.SingleAsync(p=>p.PartyId==int.Parse(id));
            if(account.PartyCode?.StartsWith(Prefix,StringComparison.Ordinal)==true &&
                (!int.TryParse(account.PartyCode[Prefix.Length..],out var linkedGroup)||!await db.ItemCategory.AnyAsync(g=>g.CategoryId==linkedGroup&&g.GroupType=="FinishedGoods"&&g.IsActive==true)))
                throw new InvalidOperationException("Select an active Finished Goods customer group.");
            return id;
        }
        if(!int.TryParse(selection[6..],out var groupId))throw new InvalidOperationException("Select a valid Finished Goods customer group.");
        var group=await db.ItemCategory.SingleOrDefaultAsync(g=>g.CategoryId==groupId&&g.GroupType=="FinishedGoods"&&g.IsActive==true)
            ??throw new InvalidOperationException("Select an active Finished Goods customer group.");
        var code=Prefix+groupId;
        var party=await db.Parties.SingleOrDefaultAsync(p=>p.PartyCode==code);
        if(party==null)
        {
            var matches=await db.Parties.Where(p=>p.PartyName==group.CategoryName&&p.Status=="active"&&(p.PartyType=="customer"||p.PartyType=="both")&&(p.PartyCode==null||p.PartyCode=="")).ToListAsync();
            if(matches.Count==1)party=matches[0];
            else {party=new Party{PartyType="customer",Status="active",OpeningBalance=0,PartyName=group.CategoryName};db.Parties.Add(party);}
            party.PartyCode=code;
        }
        if(party.Status!="active"||party.PartyType is not ("customer" or "both"))throw new InvalidOperationException("The customer account linked to this group is inactive or unavailable.");
        party.PartyName=group.CategoryName;party.Phone=group.ContactNumber;party.Address=group.Address;
        await db.SaveChangesAsync();
        return party.PartyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
