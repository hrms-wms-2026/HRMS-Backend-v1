namespace ONEVO.Application.Features.Auth.Permission;

public static class ModuleAutoGrants
{
    public static readonly IReadOnlyDictionary<string, string[]> ByModule =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core_hr"]    = ["employees:read-own", "attendance:read-own", "attendance:write-own"],
            ["employees"]  = ["employees:read-own"],
            ["leave"]      = ["leave:read-own"],
            ["attendance"] = ["attendance:read-own", "attendance:write-own"],
            ["calendar"]   = ["calendar:read"],
            ["monitoring"] = ["activity:read:self"],
            ["workforce"]  = ["workforce:dashboard"],
            ["work_management"] = ["tasks:read-own"],
        };

    private static readonly HashSet<string> AllCodes =
        new(ByModule.Values.SelectMany(v => v), StringComparer.Ordinal);

    public static bool Contains(string code) => AllCodes.Contains(code);

    public static IEnumerable<string> GetForModules(IEnumerable<string> activeModules)
    {
        foreach (var module in activeModules)
            if (ByModule.TryGetValue(module, out var perms))
                foreach (var p in perms)
                    yield return p;
    }
}
