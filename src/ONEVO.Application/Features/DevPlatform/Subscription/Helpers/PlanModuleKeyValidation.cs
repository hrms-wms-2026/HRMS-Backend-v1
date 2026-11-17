using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Helpers;

/// <summary>
/// A plan may include only active, sellable catalog modules: legacy keys are inactive after the
/// module identity migration (DEC-040), and platform baseline modules are always entitled, so they
/// never belong in included_modules_json.
/// </summary>
public static class PlanModuleKeyValidation
{
    public const string ErrorCode = "module_key_invalid";

    /// <summary>Trims, drops blanks and removes duplicates, keeping first-seen order.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> keys) =>
        keys.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Returns an error message naming every invalid key, or null when all keys are valid.</summary>
    public static string? FindInvalid(IReadOnlyList<string> keys, IReadOnlyList<ModuleCatalogItem> catalogItems)
    {
        var active = catalogItems
            .Where(m => m.IsActive)
            .Select(m => m.ModuleKey)
            .ToHashSet(StringComparer.Ordinal);

        var invalid = keys
            .Where(k => !active.Contains(k) || PlatformBaselineModules.Keys.Contains(k, StringComparer.Ordinal))
            .ToList();

        return invalid.Count == 0
            ? null
            : $"Unknown, inactive or platform baseline module keys: {string.Join(", ", invalid)}.";
    }
}
