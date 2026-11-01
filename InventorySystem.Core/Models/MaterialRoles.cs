namespace InventorySystem.Core.Models;

public static class MaterialRoles
{
    public static string Resolve(string? role, string? name)
    {
        if(!string.IsNullOrWhiteSpace(role)) return role;
        var key=new string((name??"").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key switch { "glue"=>"Glue", "silicate"=>"Silicate", "laminationglue" or "gluelamination"=>"LaminationGlue", "laminationchemical" or "laminationchemicals"=>"LaminationChemical", _=>"General" };
    }
}
