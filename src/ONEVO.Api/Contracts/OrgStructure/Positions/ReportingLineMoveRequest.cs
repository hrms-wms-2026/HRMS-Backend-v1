namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public sealed record ReportingLineMoveRequest(
    IReadOnlyCollection<Guid> SelectedEmployeeIds,
    Guid NewManagerEmployeeId,
    string? Reason = null);
