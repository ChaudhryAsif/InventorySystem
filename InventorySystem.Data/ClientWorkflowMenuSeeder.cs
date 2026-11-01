using InventorySystem.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace InventorySystem.Data;

// Explicit client workflow upgrade: idempotent, limited to the named pages/groups.
public static class ClientWorkflowMenuSeeder
{
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        var rows = await db.MenuItems.ToListAsync();
        MenuItem? Find(string controller,string action) => rows.FirstOrDefault(m=>m.Controller==controller && m.Action==action);
        MenuItem Group(string name,int order)
        {
            var item=rows.FirstOrDefault(m=>m.Controller==null && m.Name==name);
            if(item==null){item=new(){Name=name,SortOrder=order,Icon=name=="Gate Pass"?"🚚":"📦",CreatedAt=DateTime.UtcNow};db.MenuItems.Add(item);rows.Add(item);}
            return item;
        }
        var invoices=rows.FirstOrDefault(m=>m.Controller==null && (m.Name=="Sales"||m.Name=="Invoices")) ?? Group("Invoices",6);
        invoices.Name="Invoices";
        var finished=rows.FirstOrDefault(m=>m.Controller==null && m.Name=="Finished Goods") ?? Group("Finished Goods",5);
        var gate=Group("Gate Pass",8);
        await db.SaveChangesAsync();
        void Page(string controller,string action,string name,MenuItem parent,int order,string permission,string icon)
        {
            var item=Find(controller,action);
            if(item==null){item=new(){Controller=controller,Action=action,Name=name,RequiredPermission=permission,Icon=icon,CreatedAt=DateTime.UtcNow};db.MenuItems.Add(item);rows.Add(item);}
            item.ParentId=parent.Id;item.SortOrder=order;item.Name=name;
        }
        Page("Purchase","Index","Purchase Invoices",invoices,1,"Purchase.View","📝");
        Page("Report","PurchaseReport","Purchase Report",invoices,2,"Report.View","📝");
        var sale=Find("Sale","Index"); if(sale!=null){sale.ParentId=invoices.Id;sale.SortOrder=3;}
        var report=Find("Report","SaleReport");if(report!=null){report.ParentId=invoices.Id;report.SortOrder=4;}
        var raw=rows.FirstOrDefault(m=>m.Controller==null&&m.Name=="Raw Materials")??Group("Raw Materials",2);
        await db.SaveChangesAsync();
        Page("Category","Index","Groups",raw,1,"ItemCategory.View","📦");
        Page("Category","FinishedGoods","Groups",finished,1,"ItemCategory.View","📦");
        Page("Item","FinishedGoods","Items",finished,2,"Item.View","🏷️");
        var finishedStock=Find("Report","FinishedGoodsStock");if(finishedStock!=null)finishedStock.SortOrder=3;
        Page("GatePass","Outward","Outward Gate Pass",gate,1,"GatePass.View","📤");
        Page("GatePass","Inward","Inward Gate Pass",gate,2,"GatePass.View","📥");
        var roles=await db.Roles.Where(r=>r.Name=="Admin"||r.Name=="SuperAdmin").ToListAsync();
        foreach(var action in new[]{"View","Create","Edit","Delete"})
        {
            var code="GatePass."+action;
            var permission=await db.Permissions.SingleOrDefaultAsync(p=>p.Code==code);
            if(permission==null){permission=new(){Code=code,Name=action+" Gate Passes",Module="GatePass",Action=action,CreatedAt=DateTime.UtcNow};db.Permissions.Add(permission);}
            await db.SaveChangesAsync();
            foreach(var role in roles)
                if(!await db.RolePermissions.AnyAsync(r=>r.RoleId==role.Id&&r.PermissionId==permission.Id))db.RolePermissions.Add(new(){RoleId=role.Id,PermissionId=permission.Id,CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
    }
}
