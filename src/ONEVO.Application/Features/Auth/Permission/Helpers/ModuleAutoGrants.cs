namespace ONEVO.Application.Features.Auth.Permission;

public static class ModuleAutoGrants
{
    public static readonly IReadOnlyDictionary<string, string[]> ByModule =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Canonical module keys only (DEC-040, DEC-042): legacy "employees", "leave",
            // "attendance" and "activity_monitoring" are canonicalized before lookup.
            ["core_hr"]         = ["employees:read-own", "attendance:read-own", "attendance:write-own"],
            ["time_off"]        = ["leave:read-own"],
            ["time_attendance"] = ["attendance:read-own", "attendance:write-own"],
            ["calendar"]        = ["calendar:read"],
            ["monitoring"]      = ["activity:read:self"],
            ["workforce"]       = ["workforce:dashboard"],
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
