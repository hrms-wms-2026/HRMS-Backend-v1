namespace ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

/// <summary>
/// The reporting-line attendance view on the employee profile (spec D16, D17):
/// <c>GET /api/v1/employees/{employeeId}/meeting-attendance</c>.
/// <para>
/// The rows are the employee's own meeting occurrences - never the other way round: a manager sees the
/// attendance summary of the employee they manage, and only of that employee.
/// </para>
/// </summary>
/// <param name="Items">The employee's occurrences, newest first.</param>
public sealed record EmployeeMeetingAttendanceResponse(IReadOnlyList<EmployeeMeetingAttendanceItem> Items);

/// <param name="OccurrenceTaskId">The occurrence's task id - opening it goes through the meeting's own 404.</param>
/// <param name="Status">A <see cref="ONEVO.Domain.Features.WorkManagement.Meetings.Entities.MeetingAttendanceStatuses"/> value.</param>
/// <param name="Display">"Unknown" (pending, unavailable, failed), "Recorded" (provisional, synced) or "Not applicable".</param>
/// <param name="Outcome">"joined", "no_join_recorded" (only once synced) or "unknown" - never "absent".</param>
/// <param name="AttendedSeconds">The union of the person's intervals; null without any recorded join.</param>
public sealed record EmployeeMeetingAttendanceItem(
    Guid OccurrenceTaskId, DateTimeOffset StartAt, DateTimeOffset EndAt, string Title, string? OrganizerName,
    string Status, string Display, string Outcome, int? AttendedSeconds);
