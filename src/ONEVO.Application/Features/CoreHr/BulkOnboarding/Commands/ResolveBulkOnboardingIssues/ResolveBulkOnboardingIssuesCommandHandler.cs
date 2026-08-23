using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ResolveBulkOnboardingIssues;

public sealed record ResolveBulkOnboardingCreateDepartment(string Name, string? Code, Guid? ParentDepartmentId);

public sealed record ResolveBulkOnboardingCreatePosition(
    Guid DepartmentId, string Name, string Code, int Capacity, Guid? ReportsToPositionId);

public sealed record ResolveBulkOnboardingIssuesCommand(
    Guid BatchId,
    string IssueKey,
    string Action,
    string? TargetId,
    string? NewValue,
    int? WorkModeId,
    IReadOnlyList<int>? ApplyToRowNumbers,
    ResolveBulkOnboardingCreateDepartment? CreateDepartment,
    ResolveBulkOnboardingCreatePosition? CreatePosition)
    : IRequest<Result<ValidateBulkOnboardingBatchResult>>;

public sealed class ResolveBulkOnboardingIssuesCommandHandler
    : IRequestHandler<ResolveBulkOnboardingIssuesCommand, Result<ValidateBulkOnboardingBatchResult>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IBulkOnboardingValidationRunner _validationRunner;
    private readonly IWorkModeRepository _workModes;
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public ResolveBulkOnboardingIssuesCommandHandler(
        IBulkOnboardingBatchRepository batchRepository,
        IBulkOnboardingValidationRunner validationRunner,
        IWorkModeRepository workModes,
        IPositionRepository positions,
        IDepartmentRepository departments,
        IMediator mediator,
        ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _validationRunner = validationRunner;
        _workModes = workModes;
        _positions = positions;
        _departments = departments;
        _mediator = mediator;
        _currentUser = currentUser;
    }

    public async Task<Result<ValidateBulkOnboardingBatchResult>> Handle(
        ResolveBulkOnboardingIssuesCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<ValidateBulkOnboardingBatchResult>.NotFound("The batch could not be found.");

        var mapping = JsonSerializer.Deserialize<Dictionary<string, string?>>(batch.ColumnMappingJson ?? "{}")
                      ?? new Dictionary<string, string?>();

        var (issueType, importedValue) = ParseIssueKey(request.IssueKey);
        var field = FieldForIssueType(issueType);
        if (field is null)
            return Result<ValidateBulkOnboardingBatchResult>.Failure("The issue could not be recognized.");

        var state = BulkOnboardingResolutionStateSerializer.Deserialize(batch.ResolutionStateJson);
        var applyRows = request.ApplyToRowNumbers?.ToHashSet() ?? [];

        switch (request.Action)
        {
            case BulkOnboardingIssueTypes.Actions.MapExisting:
            {
                if (string.IsNullOrWhiteSpace(request.TargetId))
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("Choose a record to continue.");

                // Row-scoped remap (e.g. capacity split): override selected rows to the target label,
                // then map that label to the target id. Batch-wide remap keeps the value-map on the
                // original imported value.
                if (applyRows.Count > 0 &&
                    (issueType is BulkOnboardingIssueTypes.PositionCapacityExceeded
                        or BulkOnboardingIssueTypes.PositionNotFound
                        or BulkOnboardingIssueTypes.DepartmentNotFound
                        or BulkOnboardingIssueTypes.ChecklistTemplateNotFound))
                {
                    var label = await ResolveTargetLabelAsync(
                        batch.LegalEntityId, field, request.TargetId, ct);
                    if (label is not null)
                    {
                        await ApplyRowFieldOverridesAsync(
                            batch.Id, mapping, field, label, importedValue, applyRows, state, ct);
                        UpsertValueMap(state, field, label,
                            BulkOnboardingIssueTypes.Actions.MapExisting, request.TargetId, label);
                    }
                    else
                    {
                        UpsertValueMap(state, field, importedValue ?? string.Empty,
                            BulkOnboardingIssueTypes.Actions.MapExisting, request.TargetId, newValue: null);
                    }
                }
                else
                {
                    UpsertValueMap(state, field, importedValue ?? string.Empty,
                        BulkOnboardingIssueTypes.Actions.MapExisting, request.TargetId, newValue: null);
                }
                break;
            }
            case BulkOnboardingIssueTypes.Actions.EditImportedValue:
            {
                if (string.IsNullOrWhiteSpace(request.NewValue))
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("Enter a corrected value to continue.");

                if (applyRows.Count > 0)
                {
                    await ApplyRowFieldOverridesAsync(
                        batch.Id, mapping, field, request.NewValue, importedValue, applyRows, state, ct);
                }
                else
                {
                    UpsertValueMap(state, field, importedValue ?? string.Empty,
                        BulkOnboardingIssueTypes.Actions.EditImportedValue, targetId: null, request.NewValue);
                }
                break;
            }
            case BulkOnboardingIssueTypes.Actions.CreateDepartment:
            {
                if (!_currentUser.HasPermission("org:manage"))
                    return Result<ValidateBulkOnboardingBatchResult>.Forbidden(
                        "You do not have permission to create departments.");
                if (request.CreateDepartment is null)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("Department details are required.");

                var created = await _mediator.Send(new CreateDepartmentCommand(
                    batch.LegalEntityId,
                    request.CreateDepartment.Name,
                    request.CreateDepartment.Code,
                    request.CreateDepartment.ParentDepartmentId,
                    HeadPositionId: null), ct);
                if (!created.IsSuccess)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure(
                        created.Error ?? "Could not create the department.", created.StatusCode ?? 400);

                UpsertValueMap(state, "department", importedValue ?? request.CreateDepartment.Name,
                    BulkOnboardingIssueTypes.Actions.MapExisting, created.Value!.Id.ToString(),
                    request.CreateDepartment.Name);
                break;
            }
            case BulkOnboardingIssueTypes.Actions.CreatePosition:
            {
                if (!_currentUser.HasPermission("org:manage"))
                    return Result<ValidateBulkOnboardingBatchResult>.Forbidden(
                        "You do not have permission to create positions.");
                if (request.CreatePosition is null)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("Position details are required.");

                var created = await _mediator.Send(new CreatePositionCommand(
                    batch.LegalEntityId,
                    request.CreatePosition.DepartmentId,
                    request.CreatePosition.Name,
                    request.CreatePosition.Code,
                    request.CreatePosition.Capacity,
                    request.CreatePosition.ReportsToPositionId), ct);
                if (!created.IsSuccess)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure(
                        created.Error ?? "Could not create the position.", created.StatusCode ?? 400);

                var mapKey = importedValue ?? request.CreatePosition.Name;
                if (applyRows.Count > 0 && issueType == BulkOnboardingIssueTypes.PositionCapacityExceeded)
                {
                    await ApplyRowFieldOverridesAsync(
                        batch.Id, mapping, "position", request.CreatePosition.Name, importedValue, applyRows, state, ct);
                    mapKey = request.CreatePosition.Name;
                }

                UpsertValueMap(state, "position", mapKey,
                    BulkOnboardingIssueTypes.Actions.MapExisting, created.Value!.Id.ToString(),
                    request.CreatePosition.Name);
                break;
            }
            case BulkOnboardingIssueTypes.Actions.SetDefault:
            {
                if (field is "workMode")
                {
                    if (request.WorkModeId is null)
                        return Result<ValidateBulkOnboardingBatchResult>.Failure("Choose a work mode to continue.");
                    if (!await _workModes.ExistsActiveAsync(request.WorkModeId.Value, ct))
                        return Result<ValidateBulkOnboardingBatchResult>.Failure("The selected work mode is not available.");

                    batch.DefaultWorkModeId = request.WorkModeId.Value;
                }
                else if (field is "employmentType")
                {
                    if (string.IsNullOrWhiteSpace(request.NewValue) && string.IsNullOrWhiteSpace(request.TargetId))
                        return Result<ValidateBulkOnboardingBatchResult>.Failure(
                            "Choose an employment type to continue.");
                    batch.DefaultEmploymentType = request.NewValue ?? request.TargetId;
                }
                else
                {
                    return Result<ValidateBulkOnboardingBatchResult>.Failure(
                        "A default can only be set for work mode or employment type.");
                }
                break;
            }
            case BulkOnboardingIssueTypes.Actions.IncreaseCapacity:
            {
                if (!_currentUser.HasPermission("org:manage"))
                    return Result<ValidateBulkOnboardingBatchResult>.Forbidden(
                        "You do not have permission to update positions.");
                if (!Guid.TryParse(request.TargetId, out var positionId))
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("Choose a position to continue.");
                if (!int.TryParse(request.NewValue, out var newCapacity) || newCapacity < 1)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("Capacity must be at least 1.");

                var position = await _positions.GetByIdAsync(_currentUser.TenantId, positionId, ct);
                if (position is null || position.LegalEntityId != batch.LegalEntityId)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("The selected position could not be found.");
                if (position.DepartmentId is null)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure("The selected position has no department.");

                var updated = await _mediator.Send(new UpdatePositionCommand(
                    batch.LegalEntityId,
                    position.Id,
                    position.DepartmentId.Value,
                    position.Name,
                    position.Code ?? string.Empty,
                    newCapacity,
                    position.ReportsToPositionId), ct);
                if (!updated.IsSuccess)
                    return Result<ValidateBulkOnboardingBatchResult>.Failure(
                        updated.Error ?? "Could not update position capacity.", updated.StatusCode ?? 400);
                break;
            }
            default:
                return Result<ValidateBulkOnboardingBatchResult>.Failure("This action is not supported.");
        }

        batch.ResolutionStateJson = BulkOnboardingResolutionStateSerializer.Serialize(state);
        var result = await _validationRunner.RunAsync(batch, mapping, ct);
        await _batchRepository.SaveChangesAsync(ct);
        return Result<ValidateBulkOnboardingBatchResult>.Success(result);
    }

    private async Task<string?> ResolveTargetLabelAsync(
        Guid legalEntityId, string field, string targetId, CancellationToken ct)
    {
        if (field is "position" && Guid.TryParse(targetId, out var positionId))
        {
            var position = await _positions.GetByIdAsync(_currentUser.TenantId, positionId, ct);
            return position?.Name;
        }

        if (field is "department" && Guid.TryParse(targetId, out var departmentId))
        {
            var department = await _departments.GetByIdForLegalEntityAsync(
                _currentUser.TenantId, legalEntityId, departmentId, ct);
            return department?.Name;
        }

        if (field is "checklistTemplate")
            return null;

        if (field is "workMode" && int.TryParse(targetId, out var workModeId))
        {
            var modes = await _workModes.ListActiveAsync(ct);
            return modes.FirstOrDefault(m => m.Id == workModeId)?.Label;
        }

        return null;
    }

    private async Task ApplyRowFieldOverridesAsync(
        Guid batchId,
        IReadOnlyDictionary<string, string?> mapping,
        string field,
        string newValue,
        string? originalImported,
        ISet<int> applyRows,
        BulkOnboardingResolutionState state,
        CancellationToken ct)
    {
        var rows = await _batchRepository.ListTrackedRowsAsync(_currentUser.TenantId, batchId, ct);
        foreach (var row in rows.Where(r => applyRows.Contains(r.RowNumber)))
        {
            var originalRaw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawDataJson) ?? new();
            string? currentImported = null;
            if (mapping.TryGetValue(field, out var col) && col is not null && originalRaw.TryGetValue(col, out var v))
                currentImported = v;

            // Capacity issues use the resolved position name as importedValue; row CSV may still
            // hold the original import text. Allow override when originalImported matches either.
            if (originalImported is not null &&
                currentImported is not null &&
                !string.Equals(currentImported, originalImported, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    BulkOnboardingNameMatcher.Normalize(currentImported),
                    BulkOnboardingNameMatcher.Normalize(originalImported),
                    StringComparison.Ordinal) &&
                field is not "position")
            {
                continue;
            }

            var existing = state.RowOverrides.FirstOrDefault(o => o.RowNumber == row.RowNumber);
            if (existing is null)
            {
                existing = new BulkOnboardingRowOverride { RowNumber = row.RowNumber };
                state.RowOverrides.Add(existing);
            }

            if (!existing.OriginalFields.ContainsKey(field) && currentImported is not null)
                existing.OriginalFields[field] = currentImported;
            existing.Fields[field] = newValue;
        }
    }

    private static void UpsertValueMap(
        BulkOnboardingResolutionState state,
        string field,
        string importedValue,
        string action,
        string? targetId,
        string? newValue)
    {
        var existing = BulkOnboardingResolutionStateSerializer.FindValueMap(state, field, importedValue);
        if (existing is null)
        {
            existing = new BulkOnboardingValueMap { Field = field, ImportedValue = importedValue };
            state.ValueMaps.Add(existing);
        }

        existing.Action = action;
        existing.TargetId = targetId;
        existing.NewValue = newValue;
    }

    private static (string IssueType, string? ImportedValue) ParseIssueKey(string issueKey)
    {
        var idx = issueKey.IndexOf(':');
        if (idx < 0)
            return (issueKey, null);
        return (issueKey[..idx], issueKey[(idx + 1)..]);
    }

    private static string? FieldForIssueType(string issueType) => issueType switch
    {
        BulkOnboardingIssueTypes.DepartmentNotFound => "department",
        BulkOnboardingIssueTypes.PositionNotFound or BulkOnboardingIssueTypes.PositionCapacityExceeded => "position",
        BulkOnboardingIssueTypes.WorkModeMissing or BulkOnboardingIssueTypes.WorkModeNotFound => "workMode",
        BulkOnboardingIssueTypes.EmploymentTypeMissing or BulkOnboardingIssueTypes.EmploymentTypeNotFound => "employmentType",
        BulkOnboardingIssueTypes.ChecklistTemplateNotFound => "checklistTemplate",
        BulkOnboardingIssueTypes.DuplicateWorkEmail or BulkOnboardingIssueTypes.MissingWorkEmail => "workEmail",
        BulkOnboardingIssueTypes.DuplicateEmployeeNumber => "employeeNumber",
        BulkOnboardingIssueTypes.InvalidStartDate => "startDate",
        BulkOnboardingIssueTypes.MissingFirstName => "firstName",
        BulkOnboardingIssueTypes.MissingLastName => "lastName",
        BulkOnboardingIssueTypes.ReportingManagerRequired or BulkOnboardingIssueTypes.ReportingManagerNotFound => "reportingManager",
        _ => null
    };
}
