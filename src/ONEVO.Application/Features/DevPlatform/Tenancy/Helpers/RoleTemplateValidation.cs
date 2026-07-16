using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy;

internal static class RoleTemplateValidation
{
    public static string? ValidatePermissionCodeList(IReadOnlyList<string> codes, IReadOnlyList<Permission> resolved)
    {
        var distinct = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var code in distinct)
        {
            if (code == "*")
                return "Wildcard permission cannot be used in role templates.";

            if (ModuleAutoGrants.Contains(code))
                return $"Universal permission '{code}' cannot be stored on templates; it is granted automatically.";
        }

        if (resolved.Count != distinct.Count)
        {
            var found = resolved.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
            var missing = distinct.Where(c => !found.Contains(c)).OrderBy(c => c).ToList();
            return $"Unknown permission codes: {string.Join(", ", missing)}.";
        }

        return null;
    }
}
