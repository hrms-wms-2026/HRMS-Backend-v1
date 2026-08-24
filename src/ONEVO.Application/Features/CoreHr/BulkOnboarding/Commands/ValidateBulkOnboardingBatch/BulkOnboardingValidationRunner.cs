using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

public sealed record BulkOnboardingRowErrorDto(string Code, string Field, string Message, string? ImportedValue);

public sealed record RowValidationResultItem(
    int RowNumber,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<BulkOnboardingRowErrorDto> Errors);

public sealed record ValidateBulkOnboardingBatchResult(
    int ValidRows,
    int InvalidRows,
    int TotalRows,
    IReadOnlyList<RowValidationResultItem> Rows,
    IReadOnlyList<BulkOnboardingGroupedIssue> Issues);

/// <summary>Shared validate/revalidate logic for validate + resolve-issues.</summary>
public interface IBulkOnboardingValidationRunner
{
    Task<ValidateBulkOnboardingBatchResult> RunAsync(
        BulkOnboardingBatch batch,
        IReadOnlyDictionary<string, string?> mapping,
        CancellationToken ct);
}

public sealed class BulkOnboardingValidationRunner : IBulkOnboardingValidationRunner
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IBulkOnboardingRowValidator _rowValidator;
    private readonly IDepartmentRepository _departments;
    private readonly IPositionRepository _positions;
    private readonly IPositionAssignmentRepository _positionAssignments;
    private readonly IWorkModeRepository _workModes;
    private readonly IChecklistTemplateRepository _checklistTemplates;
    private readonly ICurrentUser _currentUser;

    public BulkOnboardingValidationRunner(
        IBulkOnboardingBatchRepository batchRepository,
        IBulkOnboardingRowValidator rowValidator,
        IDepartmentRepository departments,
        IPositionRepository positions,
        IPositionAssignmentRepository positionAssignments,
        IWorkModeRepository workModes,
        IChecklistTemplateRepository checklistTemplates,
        ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _rowValidator = rowValidator;
        _departments = departments;
        _positions = positions;
        _positionAssignments = positionAssignments;
        _workModes = workModes;
        _checklistTemplates = checklistTemplates;
        _currentUser = currentUser;
    }

    public async Task<ValidateBulkOnboardingBatchResult> RunAsync(
        BulkOnboardingBatch batch,
        IReadOnlyDictionary<string, string?> mapping,
        CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var resolutionState = BulkOnboardingResolutionStateSerializer.Deserialize(batch.ResolutionStateJson);
        var rows = await _batchRepository.ListTrackedRowsAsync(tenantId, batch.Id, ct);
        var emailsSeen = new HashSet<string>();
        var results = new List<RowValidationResultItem>();
        var errorPairs = new List<(int RowNumber, RowValidationError Error)>();
        var validCount = 0;
        var invalidCount = 0;
        var positionBoundRows = new List<(BulkOnboardingBatchRow Row, Guid PositionId, Guid? DepartmentId)>();

        var positionsCache = await _positions.ListByLegalEntityAsync(
            tenantId, batch.LegalEntityId, includeInactive: false, departmentId: null, ct);
        var positionsById = positionsCache.ToDictionary(p => p.Id);

        foreach (var row in rows.OrderBy(r => r.RowNumber))
        {
            var originalRaw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawDataJson) ?? new();
            var effectiveRaw = BulkOnboardingResolutionStateSerializer.BuildEffectiveRawData(
                originalRaw, mapping, resolutionState, row.RowNumber);

            var outcome = await _rowValidator.ValidateRowAsync(
                tenantId, batch, effectiveRaw, mapping, emailsSeen, ct, resolutionState);

            row.ResolvedDepartmentId = outcome.DepartmentId;
            row.ResolvedPositionId = outcome.PositionId;
            row.ResolvedReportsToEmployeeId = outcome.ReportsToEmployeeId;
            row.ResolvedTemplateId = outcome.TemplateId;
            row.ResolvedWorkModeId = outcome.WorkModeId;
            row.Status = outcome.IsValid ? BulkOnboardingBatchRowStatus.Valid : BulkOnboardingBatchRowStatus.Invalid;
            row.ErrorMessage = outcome.ErrorMessage;

            var errors = outcome.Error is null
                ? Array.Empty<BulkOnboardingRowErrorDto>()
                : new[]
                {
                    new BulkOnboardingRowErrorDto(
                        outcome.Error.Code, outcome.Error.Field, outcome.Error.Message, outcome.Error.ImportedValue)
                };

            if (outcome.IsValid) validCount++;
            else
            {
                invalidCount++;
                if (outcome.Error is not null)
                    errorPairs.Add((row.RowNumber, outcome.Error));
            }

            results.Add(new RowValidationResultItem(row.RowNumber, row.Status, row.ErrorMessage, errors));

            if (outcome.IsValid && outcome.PositionId is { } positionId && positionsById.ContainsKey(positionId))
                positionBoundRows.Add((row, positionId, outcome.DepartmentId));
        }

        var canManageOrg = _currentUser.HasPermission("org:manage");
        var capacityIssues = await ApplyBatchPositionCapacityAsync(
            tenantId, batch.LegalEntityId, positionsById, positionBoundRows, results, errorPairs, canManageOrg, ct);

        validCount = results.Count(r => r.Status == BulkOnboardingBatchRowStatus.Valid);
        invalidCount = results.Count - validCount;

        batch.Status = BulkOnboardingBatchStatus.Validated;
        batch.ValidRows = validCount;
        batch.InvalidRows = invalidCount;
        batch.ColumnMappingJson = JsonSerializer.Serialize(mapping);

        var catalog = await BuildSuggestionCatalogAsync(tenantId, batch.LegalEntityId, ct);
        var issues = BulkOnboardingIssueGrouper.Group(errorPairs, catalog, canManageOrg).ToList();

        for (var i = 0; i < issues.Count; i++)
        {
            var issue = issues[i];
            if (issue.IssueType != BulkOnboardingIssueTypes.PositionCapacityExceeded)
                continue;
            if (!capacityIssues.TryGetValue(issue.IssueKey, out var enriched))
                continue;
            issues[i] = issue with { Context = enriched.Context, Suggestions = enriched.Suggestions };
        }

        return new ValidateBulkOnboardingBatchResult(validCount, invalidCount, batch.TotalRows, results, issues);
    }

    private async Task<Dictionary<string, (BulkOnboardingIssueContext Context, IReadOnlyList<BulkOnboardingIssueSuggestion> Suggestions)>> ApplyBatchPositionCapacityAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyDictionary<Guid, Position> positionsById,
        IReadOnlyList<(BulkOnboardingBatchRow Row, Guid PositionId, Guid? DepartmentId)> positionBoundRows,
        List<RowValidationResultItem> results,
        List<(int RowNumber, RowValidationError Error)> errorPairs,
        bool canManageOrg,
        CancellationToken ct)
    {
        var capacityByKey = new Dictionary<string, (BulkOnboardingIssueContext Context, IReadOnlyList<BulkOnboardingIssueSuggestion> Suggestions)>(
            StringComparer.OrdinalIgnoreCase);

        if (positionBoundRows.Count == 0)
            return capacityByKey;

        var byPosition = positionBoundRows.GroupBy(x => x.PositionId);
        var occupancy = await _positionAssignments.GetOccupancyPreviewsAsync(
            tenantId, byPosition.Select(g => g.Key).ToList(), previewLimit: 1, ct);

        var departments = await _departments.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: false, ct);
        var departmentNames = departments.ToDictionary(d => d.Id, d => d.Name);

        foreach (var group in byPosition)
        {
            if (!positionsById.TryGetValue(group.Key, out var position))
                continue;

            var currentPrimary = occupancy.TryGetValue(position.Id, out var preview)
                ? preview.AssignedCount
                : await _positionAssignments.CountActiveAsync(tenantId, position.Id, ct);
            var available = Math.Max(0, position.MaxOccupancy - currentPrimary);
            var required = group.Count();
            if (available >= required)
                continue;

            string? departmentName = null;
            if (position.DepartmentId is { } deptId)
                departmentNames.TryGetValue(deptId, out departmentName);

            var issueKey = $"{BulkOnboardingIssueTypes.PositionCapacityExceeded}:{position.Name}";
            var message =
                $"This position has {available} available seat{(available == 1 ? "" : "s")}, but {required} imported employees need it.";

            var context = new BulkOnboardingIssueContext(
                position.Id.ToString(),
                position.Name,
                position.DepartmentId?.ToString(),
                departmentName,
                position.MaxOccupancy,
                currentPrimary,
                available,
                required,
                canManageOrg);

            var alternateSuggestions = await BuildAlternatePositionSuggestionsAsync(
                tenantId, position, availableNeeded: 1, ct);

            capacityByKey[issueKey] = (context, alternateSuggestions);

            foreach (var (row, _, _) in group)
            {
                row.Status = BulkOnboardingBatchRowStatus.Invalid;
                row.ErrorMessage = message;

                var error = new RowValidationError(
                    BulkOnboardingIssueTypes.PositionCapacityExceeded,
                    "position",
                    message,
                    position.Name);

                errorPairs.RemoveAll(e => e.RowNumber == row.RowNumber);
                errorPairs.Add((row.RowNumber, error));

                var idx = results.FindIndex(r => r.RowNumber == row.RowNumber);
                if (idx >= 0)
                {
                    results[idx] = new RowValidationResultItem(
                        row.RowNumber,
                        BulkOnboardingBatchRowStatus.Invalid,
                        message,
                        [new BulkOnboardingRowErrorDto(error.Code, error.Field, error.Message, error.ImportedValue)]);
                }
            }
        }

        return capacityByKey;
    }

    private async Task<IReadOnlyList<BulkOnboardingIssueSuggestion>> BuildAlternatePositionSuggestionsAsync(
        Guid tenantId, Position fullPosition, int availableNeeded, CancellationToken ct)
    {
        if (fullPosition.LegalEntityId is not { } legalEntityId)
            return [];

        var candidates = await _positions.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: false, fullPosition.DepartmentId, ct);
        var others = candidates.Where(p => p.Id != fullPosition.Id).Take(20).ToList();
        if (others.Count == 0)
            return [];

        var occupancy = await _positionAssignments.GetOccupancyPreviewsAsync(
            tenantId, others.Select(p => p.Id).ToList(), previewLimit: 1, ct);

        return others
            .Select(p =>
            {
                var assigned = occupancy.TryGetValue(p.Id, out var preview) ? preview.AssignedCount : 0;
                var seats = Math.Max(0, p.MaxOccupancy - assigned);
                return (Position: p, Seats: seats);
            })
            .Where(x => x.Seats >= availableNeeded)
            .OrderByDescending(x => x.Seats)
            .Take(5)
            .Select(x => new BulkOnboardingIssueSuggestion(
                x.Position.Id.ToString(),
                x.Seats <= 0
                    ? $"{x.Position.Name} (No vacancy)"
                    : $"{x.Position.Name} ({x.Seats} seat{(x.Seats == 1 ? "" : "s")} available)",
                "available"))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Label)>>> BuildSuggestionCatalogAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct)
    {
        var departments = await _departments.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: false, ct);
        var positions = await _positions.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: false, departmentId: null, ct);
        var workModes = await _workModes.ListActiveAsync(ct);
        var templates = await _checklistTemplates.ListOnboardingMatchesAsync(tenantId, legalEntityId, null, null, ct);

        return new Dictionary<string, IReadOnlyList<(string Id, string Label)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["department"] = departments.Select(d => (d.Id.ToString(), d.Name)).ToList(),
            ["position"] = positions.Select(p => (p.Id.ToString(), p.Name)).ToList(),
            ["workMode"] = workModes.Select(w => (w.Id.ToString(), w.Label)).Concat(
                workModes.Select(w => (w.Id.ToString(), w.Code))).Distinct().ToList(),
            ["checklistTemplate"] = templates.Select(t => (t.Template.Id.ToString(), t.Template.Name)).ToList(),
            ["employmentType"] =
            [
                ("full_time", "Full-time"),
                ("part_time", "Part-time"),
                ("contract", "Contract"),
                ("intern", "Intern")
            ]
        };
    }
}
