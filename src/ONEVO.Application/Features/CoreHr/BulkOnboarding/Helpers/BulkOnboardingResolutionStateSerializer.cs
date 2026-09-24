using System.Text.Json;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public static class BulkOnboardingResolutionStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static BulkOnboardingResolutionState Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new BulkOnboardingResolutionState();

        return JsonSerializer.Deserialize<BulkOnboardingResolutionState>(json, Options)
               ?? new BulkOnboardingResolutionState();
    }

    public static string Serialize(BulkOnboardingResolutionState state) =>
        JsonSerializer.Serialize(state, Options);

    /// <summary>
    /// Builds effective cell values for a row: original raw data + row field overrides
    /// (written against system field names, applied onto mapped CSV columns) + value-map edits.
    /// Original RawDataJson is never mutated by callers of this helper.
    /// </summary>
    public static Dictionary<string, string> BuildEffectiveRawData(
        Dictionary<string, string> originalRaw,
        IReadOnlyDictionary<string, string?> mapping,
        BulkOnboardingResolutionState state,
        int rowNumber)
    {
        var effective = new Dictionary<string, string>(originalRaw, StringComparer.OrdinalIgnoreCase);

        var rowOverride = state.RowOverrides.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (rowOverride is not null)
        {
            foreach (var (systemField, value) in rowOverride.Fields)
            {
                if (!mapping.TryGetValue(systemField, out var column) || column is null)
                {
                    // No mapped column — synthesize a synthetic column key so Get() can still read it.
                    var synthetic = $"__override_{systemField}";
                    effective[synthetic] = value;
                    continue;
                }

                effective[column] = value;
            }
        }

        foreach (var map in state.ValueMaps.Where(m =>
                     string.Equals(m.Action, BulkOnboardingIssueTypes.Actions.EditImportedValue, StringComparison.Ordinal)))
        {
            if (string.IsNullOrWhiteSpace(map.NewValue))
                continue;
            if (!mapping.TryGetValue(map.Field, out var column) || column is null)
                continue;
            if (!effective.TryGetValue(column, out var current))
                continue;
            if (!string.Equals(current, map.ImportedValue, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(BulkOnboardingNameMatcher.Normalize(current), BulkOnboardingNameMatcher.Normalize(map.ImportedValue), StringComparison.Ordinal))
                continue;

            effective[column] = map.NewValue;
        }

        return effective;
    }

    public static BulkOnboardingValueMap? FindValueMap(
        BulkOnboardingResolutionState state, string field, string? importedValue)
    {
        if (string.IsNullOrWhiteSpace(importedValue))
            return null;

        return state.ValueMaps.FirstOrDefault(m =>
            string.Equals(m.Field, field, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(m.ImportedValue, importedValue, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 BulkOnboardingNameMatcher.Normalize(m.ImportedValue),
                 BulkOnboardingNameMatcher.Normalize(importedValue),
                 StringComparison.Ordinal)));
    }
}
