namespace ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

// The bulk invite preview contract (meetings-as-tasks Phase 2, spec 5.2 "Bulk invite", D8). Serialized with
// JsonSerializerDefaults.Web (camelCase).

/// <summary>
/// POST /api/v1/meetings/bulk-preview. The organizer picks departments, managers' teams and individual people;
/// the preview expands the groups into a snapshot and reports the counts.
/// </summary>
/// <param name="ProjectId">The meeting's project. Null means an Office meeting (calendar:write).</param>
/// <param name="ObjectiveId">The meeting's module.</param>
/// <param name="DepartmentIds">Departments to expand - a department's own employees, not its sub-departments'.</param>
/// <param name="ManagerEmployeeIds">Managers whose direct reports are expanded.</param>
/// <param name="EmployeeIds">Individual people to include.</param>
/// <param name="SeriesId">The series to add attendees to. Null means a new meeting, behind the create gate.</param>
/// <param name="Platform">"microsoft_teams" or "zoom" - only needed for the capacity warning.</param>
public sealed record MeetingBulkPreviewRequest(
    Guid? ProjectId, Guid? ObjectiveId,
    IReadOnlyList<Guid> DepartmentIds, IReadOnlyList<Guid> ManagerEmployeeIds, IReadOnlyList<Guid> EmployeeIds,
    Guid? SeriesId, string? Platform)
{
    // A left-out list means "none picked", as in CreateMeetingRequest.
    public IReadOnlyList<Guid> DepartmentIds { get; init; } = DepartmentIds ?? [];
    public IReadOnlyList<Guid> ManagerEmployeeIds { get; init; } = ManagerEmployeeIds ?? [];
    public IReadOnlyList<Guid> EmployeeIds { get; init; } = EmployeeIds ?? [];
}

/// <summary>One person in the preview - the participant picker's fields only.</summary>
public sealed record MeetingBulkPerson(Guid EmployeeId, string Name, string? Email, string? DepartmentName);

/// <summary>
/// The preview's answer: the people, the four counts and - above the provider's capacity - a warning.
/// </summary>
/// <param name="Selected">Every id the organizer picked, before dedup.</param>
/// <param name="DuplicatesRemoved">The repeats that were removed.</param>
/// <param name="InactiveExcluded">The people who are not active (or have no employee record) and were left out.</param>
/// <param name="AlreadyInvited">The people already on the meeting, including the organizer - they are not offered again.</param>
/// <param name="CapacityWarning">A warning above the provider's capacity. Never a cap: the organizer may go ahead.</param>
public sealed record MeetingBulkPreviewResponse(
    IReadOnlyList<MeetingBulkPerson> People,
    int Selected, int DuplicatesRemoved, int InactiveExcluded, int AlreadyInvited,
    string? CapacityWarning);
