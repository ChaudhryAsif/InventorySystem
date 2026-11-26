namespace InventorySystem.Core.Services;

// Decides whether the browser should hide write buttons on a page. Deliberately narrow: only a user who has NO write
// permission at all in the page's module is treated as view-only. A user with any write right (even just Create) is
// left alone, so a button that still works for them is never hidden. The server enforces every permission either way.
public static class ViewOnlyMode
{
    public const string AllWriteVerbs = "create edit delete post";

    // moduleCodes: every permission code that exists for the module; userCodes: the codes this user holds.
    public static string DenyVerbs(string module, IEnumerable<string> moduleCodes, IEnumerable<string> userCodes)
    {
        var prefix = module + ".";
        var writeCodes = moduleCodes.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !c.Equals(prefix + "View", StringComparison.OrdinalIgnoreCase)).ToList();
        if (writeCodes.Count == 0) return "";                                   // nothing can be granted here, so there is nothing to hide
        var held = new HashSet<string>(userCodes, StringComparer.OrdinalIgnoreCase);
        return writeCodes.Any(held.Contains) ? "" : AllWriteVerbs;
    }
}
