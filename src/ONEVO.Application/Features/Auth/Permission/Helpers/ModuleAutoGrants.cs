using ONEVO.Application.Common.Authorization;

namespace ONEVO.Application.Features.Auth.Permission;

public static class ModuleAutoGrants
{
    public static readonly IReadOnlyDictionary<string, string[]> ByModule =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core_hr"]    = [PermissionCatalog.Employees.ReadOwn, PermissionCatalog.Attendance.ReadOwn, PermissionCatalog.Attendance.WriteOwn],
            ["employees"]  = [PermissionCatalog.Employees.ReadOwn],
            ["leave"]      = [PermissionCatalog.Leave.ReadOwn],
            ["attendance"] = [PermissionCatalog.Attendance.ReadOwn, PermissionCatalog.Attendance.WriteOwn],
            ["calendar"]   = [PermissionCatalog.Calendar.Read],
            ["activity_monitoring"] = [PermissionCatalog.Activity.ReadSelf],
            ["workforce"]  = [PermissionCatalog.Workforce.Dashboard],
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
