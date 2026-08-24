namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record ValidateBulkOnboardingBatchRequest(IReadOnlyDictionary<string, string?> Mapping);

public sealed record BulkOnboardingRowErrorViewModel(
    string Code, string Field, string Message, string? ImportedValue);

public sealed record BulkOnboardingIssueSuggestionViewModel(
    string Id, string Label, string Confidence);

public sealed record BulkOnboardingIssueContextViewModel(
    string? PositionId,
    string? PositionName,
    string? DepartmentId,
    string? DepartmentName,
    int? MaxOccupancy,
    int? CurrentPrimaryAssignments,
    int? AvailableSeats,
    int? RequiredSeatsInBatch,
    bool CanIncreaseCapacity);

public sealed record BulkOnboardingGroupedIssueViewModel(
    string IssueKey,
    string IssueType,
    string Field,
    string? ImportedValue,
    IReadOnlyList<int> AffectedRowNumbers,
    int AffectedRowCount,
    IReadOnlyList<BulkOnboardingIssueSuggestionViewModel> Suggestions,
    IReadOnlyList<string> AllowedActions,
    BulkOnboardingIssueContextViewModel? Context);

public sealed record BulkOnboardingRowValidationItem(
    int RowNumber,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<BulkOnboardingRowErrorViewModel> Errors);

public sealed record ValidateBulkOnboardingBatchResponse(
    int ValidRows,
    int InvalidRows,
    int TotalRows,
    IReadOnlyList<BulkOnboardingRowValidationItem> Rows,
    IReadOnlyList<BulkOnboardingGroupedIssueViewModel> Issues);

public sealed record ResolveBulkOnboardingCreateDepartmentRequest(
    string Name, string? Code, Guid? ParentDepartmentId);

public sealed record ResolveBulkOnboardingCreatePositionRequest(
    Guid DepartmentId, string Name, string Code, int Capacity, Guid? ReportsToPositionId);

public sealed record ResolveBulkOnboardingIssuesRequest(
    string IssueKey,
    string Action,
    string? TargetId,
    string? NewValue,
    int? WorkModeId,
    IReadOnlyList<int>? ApplyToRowNumbers,
    ResolveBulkOnboardingCreateDepartmentRequest? Create,
    ResolveBulkOnboardingCreatePositionRequest? CreatePosition);
