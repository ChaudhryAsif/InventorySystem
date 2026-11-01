using System.Reflection;
using InventorySystem.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace InventorySystem.Tests;
public class InventoryCreateNotLockedTests
{
    [Theory][InlineData("Item")][InlineData("Category")][InlineData("Sale")][InlineData("SaleReturn")][InlineData("Purchase")][InlineData("PurchaseReturn")][InlineData("CostSheet")][InlineData("Production")][InlineData("GatePass")][InlineData("StockAdjustment")]
    public void SavingANewRecordIsNeverLocked(string controller)
    {
        var type = typeof(InventorySystem.Controllers.ItemController).Assembly.GetType($"InventorySystem.Controllers.{controller}Controller")!;
        var save = type.GetMethods().Single(m => m.Name == "Save" && m.GetCustomAttributes<HttpPostAttribute>().Any() || m.Name == "Save" && m.ReturnType.IsGenericType);
        var model = Activator.CreateInstance(save.GetParameters()[0].ParameterType);
        // A new record may still reference other records (a production order names its cost sheet).
        foreach (var p in model!.GetType().GetProperties().Where(p => p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(long) || p.PropertyType == typeof(int?) || p.PropertyType == typeof(long?)) && p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && p.Name != $"{controller}Id" && p.Name != "Id" && p.Name != "ItemID"))
            p.SetValue(model, Convert.ChangeType(1, Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
        Assert.False(OperationFilter.InventoryEditBlocked(controller, "Save", true, [model]));
    }
}
