using System.Text.Json;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.Storage.Quota.Helpers;

/// <summary>
/// Computes the storage allowance (in bytes) contributed by a tenant's selected
/// modules. Each storage-consuming module carries a company-size storage
/// reference in <c>module_catalog.storage_reference</c>, shaped like the pricing
/// reference brackets:
/// <c>[{ "min_employees": 1, "max_employees": 50, "storage_gb": 25 }]</c>.
///
/// This is a fail-safe calculator: a module with an empty, malformed, or
/// non-matching storage reference contributes zero bytes. It never grants
/// storage it cannot prove from catalog data.
/// </summary>
public static class ModuleStorageAllowanceCalculator
{
    private const long BytesPerGigabyte = 1024L * 1024L * 1024L;

    public static long CalculateTotalBytes(
        IReadOnlyList<ModuleCatalogItem> catalog,
        IReadOnlyList<string> selectedModuleKeys,
        string companySizeRange)
    {
        if (selectedModuleKeys.Count == 0)
            return 0;

        if (!TryParseSizeRange(companySizeRange, out var rangeMin, out var rangeMax))
            return 0;

        long totalBytes = 0;

        foreach (var moduleKey in selectedModuleKeys)
        {
            ModuleCatalogItem? module = null;
            foreach (var candidate in catalog)
            {
                if (candidate.ModuleKey == moduleKey)
                {
                    module = candidate;
                    break;
                }
            }

            if (module is null)
                continue;

            if (!module.IsStorageConsuming)
                continue;

            var moduleGigabytes = ResolveStorageGigabytes(module.StorageReference, rangeMin, rangeMax);
            totalBytes += moduleGigabytes * BytesPerGigabyte;
        }

        return totalBytes;
    }

    private static long ResolveStorageGigabytes(string storageReferenceJson, int rangeMin, int rangeMax)
    {
        if (string.IsNullOrWhiteSpace(storageReferenceJson))
            return 0;

        List<StorageBracket>? brackets;
        try
        {
            brackets = JsonSerializer.Deserialize<List<StorageBracket>>(
                storageReferenceJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (JsonException)
        {
            return 0;
        }

        if (brackets is null)
            return 0;

        foreach (var bracket in brackets)
        {
            if (bracket.StorageGb <= 0)
                continue;

            if (rangeMax == -1)
            {
                if (bracket.MaxEmployees == -1)
                    return bracket.StorageGb;
            }
            else if (bracket.MinEmployees <= rangeMin && bracket.MaxEmployees >= rangeMax)
            {
                return bracket.StorageGb;
            }
        }

        return 0;
    }

    private static bool TryParseSizeRange(string range, out int min, out int max)
    {
        min = 0;
        max = 0;

        if (string.IsNullOrWhiteSpace(range))
            return false;

        if (range.EndsWith('+'))
        {
            if (!int.TryParse(range.TrimEnd('+'), out min))
                return false;
            max = -1;
            return true;
        }

        var parts = range.Split('-');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out min) &&
            int.TryParse(parts[1], out max))
            return true;

        return false;
    }

    private sealed record StorageBracket(int MinEmployees, int MaxEmployees, long StorageGb);
}
