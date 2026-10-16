namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public sealed record ReportingLineMovePersonResponse(
    Guid EmployeeId,
    string Name,
    string PositionName);

public sealed record ReportingLineMoveChangeResponse(
    ReportingLineMovePersonResponse Employee,
    ReportingLineMovePersonResponse? CurrentManager,
    ReportingLineMovePersonResponse NewManager);

/// <summary>
/// Impact of moving one or more selected people as reporting branches. Only the top-most
/// selected person in each branch changes manager; relationships below those roots are preserved.
/// </summary>
public sealed record ReportingLineMoveImpactResponse(
    int SelectedCount,
    int DirectRelationshipsChanged,
    int PreservedRelationships,
    int TotalEmployeesAffected,
    IReadOnlyList<ReportingLineMoveChangeResponse> Changes);
