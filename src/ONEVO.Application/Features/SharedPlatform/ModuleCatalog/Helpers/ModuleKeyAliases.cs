namespace ONEVO.Application.Features.SharedPlatform.ModuleCatalog.Helpers;

/// <summary>
/// Temporary legacy → canonical module key map (DEC-040, DEC-041, DEC-042, DEC-048).
/// Only for the module identity migration: it normalizes keys read from persisted data and
/// expands canonical keys for clients that still check legacy keys. Remove it once every
/// client uses canonical keys (feature-key Phase 1 plan, Task 23). Do not add new uses.
/// </summary>
public static class ModuleKeyAliases
{
    public static readonly IReadOnlyDictionary<string, string> LegacyToCanonical =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["org_structure"] = "org",
            ["leave"] = "time_off",
            ["activity_monitoring"] = "monitoring",
            ["desktop_agent_gateway"] = "monitoring",
            ["discrepancy_engine"] = "monitoring",
            ["exception_engine"] = "monitoring",
            ["identity_verification"] = "verification",
            ["productivity_analytics"] = "analytics",
            ["worksync_foundation"] = "work_management",
            ["projects"] = "work_management",
            ["objectives_milestones"] = "work_management",
            ["tasks"] = "work_management",
            ["boards"] = "work_management",
            ["planning_sprints"] = "work_management",
            ["attendance"] = "time_attendance",
            ["employees"] = "core_hr",
        };

    public static string Canonicalize(string key) =>
        LegacyToCanonical.TryGetValue(key, out var canonical) ? canonical : key;

    /// <summary>Trims, drops blanks, maps legacy keys and removes duplicates, keeping first-seen order.</summary>
    public static IReadOnlyList<string> CanonicalizeAll(IEnumerable<string?> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in keys)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var canonical = Canonicalize(raw.Trim());
            if (seen.Add(canonical))
                result.Add(canonical);
        }
        return result;
    }

    /// <summary>Canonical keys plus every legacy key that maps to them, for transition-period clients.</summary>
    public static IReadOnlyList<string> ExpandWithLegacyAliases(IEnumerable<string> canonicalKeys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var key in canonicalKeys)
        {
            if (seen.Add(key))
                result.Add(key);
            foreach (var (legacy, canonical) in LegacyToCanonical)
                if (canonical == key && seen.Add(legacy))
                    result.Add(legacy);
        }
        return result;
    }
}
