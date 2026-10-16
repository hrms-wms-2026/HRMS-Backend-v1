namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

/// <summary>
/// One card in the Positions "People" view: a person holding a position, or a vacant position.
/// Ids are "e:{employeeId}" / "p:{positionId}"; ParentId null = top of the chart.
/// </summary>
/// <param name="ReportingLine">
/// How this card's parent was resolved: "position" (the only holder of the position above, or
/// the explicit pick among several), "default" (several holders above and nobody was picked, so
/// the first holder is shown), "custom" (a one-off manager from a different reporting line).
/// Null for top-of-chart cards.
/// </param>
public sealed record PositionPeopleChartNodeResponse(
    string Id,
    string? ParentId,
    string Kind,
    Guid PositionId,
    string PositionName,
    string? DepartmentName,
    Guid? EmployeeId,
    string? FirstName,
    string? LastName,
    Guid? AvatarFileId,
    string? ReportingLine,
    int MaxOccupancy,
    int HolderCount,
    // Invited but hasn't accepted yet (a planned seat): placed like everyone else, but never
    // anyone's manager or default holder until the seat is activated.
    bool IsPending = false,
    string? WorkEmail = null);
