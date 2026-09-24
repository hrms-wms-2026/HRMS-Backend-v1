using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;

public sealed record BulkOnboardingIssueSuggestion(string Id, string Label, string Confidence);

/// <summary>Safe display metadata for issue cards (capacity conflicts, etc.). No internal entity names.</summary>
public sealed record BulkOnboardingIssueContext(
    string? PositionId,
    string? PositionName,
    string? DepartmentId,
    string? DepartmentName,
    int? MaxOccupancy,
    int? CurrentPrimaryAssignments,
    int? AvailableSeats,
    int? RequiredSeatsInBatch,
    bool CanIncreaseCapacity);

public sealed record BulkOnboardingGroupedIssue(
    string IssueKey,
    string IssueType,
    string Field,
    string? ImportedValue,
    IReadOnlyList<int> AffectedRowNumbers,
    int AffectedRowCount,
    IReadOnlyList<BulkOnboardingIssueSuggestion> Suggestions,
    IReadOnlyList<string> AllowedActions,
    BulkOnboardingIssueContext? Context = null);

public static class BulkOnboardingIssueGrouper
{
    public static IReadOnlyList<BulkOnboardingGroupedIssue> Group(
        IReadOnlyList<(int RowNumber, RowValidationError Error)> rowErrors,
        IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Label)>> suggestionCatalog,
        bool canManageOrg)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rowNumber, error) in rowErrors)
        {
            var imported = error.ImportedValue ?? string.Empty;
            var key = string.IsNullOrEmpty(imported)
                ? error.Code
                : $"{error.Code}:{imported}";

            if (!groups.TryGetValue(key, out var rows))
            {
                rows = [];
                groups[key] = rows;
            }

            if (!rows.Contains(rowNumber))
                rows.Add(rowNumber);
        }

        var result = new List<BulkOnboardingGroupedIssue>();
        foreach (var (key, rows) in groups.OrderBy(g => g.Value.Min()).ThenBy(g => g.Key))
        {
            var sample = rowErrors.First(e =>
            {
                var imported = e.Error.ImportedValue ?? string.Empty;
                var sampleKey = string.IsNullOrEmpty(imported) ? e.Error.Code : $"{e.Error.Code}:{imported}";
                return string.Equals(sampleKey, key, StringComparison.OrdinalIgnoreCase);
            }).Error;

            var suggestions = BuildSuggestions(sample.Code, sample.Field, sample.ImportedValue, suggestionCatalog);
            var actions = AllowedActionsFor(sample.Code, canManageOrg);

            result.Add(new BulkOnboardingGroupedIssue(
                key,
                sample.Code,
                sample.Field,
                sample.ImportedValue,
                rows.OrderBy(n => n).ToList(),
                rows.Count,
                suggestions,
                actions,
                BuildContext(sample)));
        }

        return result;
    }

    private static BulkOnboardingIssueContext? BuildContext(RowValidationError sample)
    {
        if (sample.Code is BulkOnboardingIssueTypes.ReportingManagerRequired
            or BulkOnboardingIssueTypes.ReportingManagerNotFound)
        {
            if (string.IsNullOrWhiteSpace(sample.RelatedEntityId))
                return null;
            return new BulkOnboardingIssueContext(
                sample.RelatedEntityId,
                PositionName: null,
                DepartmentId: null,
                DepartmentName: null,
                MaxOccupancy: null,
                CurrentPrimaryAssignments: null,
                AvailableSeats: null,
                RequiredSeatsInBatch: null,
                CanIncreaseCapacity: false);
        }

        return null;
    }

    public static IReadOnlyList<string> AllowedActionsFor(string issueType, bool canManageOrg)
    {
        return issueType switch
        {
            BulkOnboardingIssueTypes.DepartmentNotFound => canManageOrg
                ? [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue, BulkOnboardingIssueTypes.Actions.CreateDepartment]
                : [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue],
            BulkOnboardingIssueTypes.PositionNotFound => canManageOrg
                ? [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue, BulkOnboardingIssueTypes.Actions.CreatePosition]
                : [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue],
            BulkOnboardingIssueTypes.WorkModeMissing => [BulkOnboardingIssueTypes.Actions.SetDefault],
            BulkOnboardingIssueTypes.WorkModeNotFound =>
                [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue, BulkOnboardingIssueTypes.Actions.SetDefault],
            BulkOnboardingIssueTypes.EmploymentTypeNotFound or BulkOnboardingIssueTypes.EmploymentTypeMissing =>
                [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue, BulkOnboardingIssueTypes.Actions.SetDefault],
            BulkOnboardingIssueTypes.ChecklistTemplateNotFound =>
                [BulkOnboardingIssueTypes.Actions.MapExisting, BulkOnboardingIssueTypes.Actions.EditImportedValue],
            BulkOnboardingIssueTypes.PositionCapacityExceeded => canManageOrg
                ? [
                    BulkOnboardingIssueTypes.Actions.MapExisting,
                    BulkOnboardingIssueTypes.Actions.CreatePosition,
                    BulkOnboardingIssueTypes.Actions.IncreaseCapacity
                ]
                : [BulkOnboardingIssueTypes.Actions.MapExisting],
            _ when BulkOnboardingIssueTypes.IsRowEdit(issueType) =>
                [BulkOnboardingIssueTypes.Actions.EditImportedValue],
            _ => [BulkOnboardingIssueTypes.Actions.EditImportedValue]
        };
    }

    private static IReadOnlyList<BulkOnboardingIssueSuggestion> BuildSuggestions(
        string issueType,
        string field,
        string? importedValue,
        IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Label)>> suggestionCatalog)
    {
        if (string.IsNullOrWhiteSpace(importedValue))
            return [];

        if (!suggestionCatalog.TryGetValue(field, out var candidates) || candidates.Count == 0)
            return [];

        if (issueType is not (
            BulkOnboardingIssueTypes.DepartmentNotFound or
            BulkOnboardingIssueTypes.PositionNotFound or
            BulkOnboardingIssueTypes.WorkModeNotFound or
            BulkOnboardingIssueTypes.EmploymentTypeNotFound or
            BulkOnboardingIssueTypes.ChecklistTemplateNotFound))
            return [];

        var match = BulkOnboardingNameMatcher.FindBest(importedValue, candidates.Select(c => c.Label));
        if (match is null)
            return [];

        var entity = candidates.FirstOrDefault(c => string.Equals(c.Label, match.Label, StringComparison.Ordinal));
        if (entity.Id is null)
            return [];

        return [new BulkOnboardingIssueSuggestion(entity.Id, match.Label, match.Confidence)];
    }
}
