using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

public static class MeetingAttendeeKinds
{
    public const string Employee = "employee";
    public const string Guest = "guest";
}

public static class MeetingAttendeeChanges
{
    public const string Add = "add";
    public const string Remove = "remove";
}

/// <summary>
/// One occurrence's change to the series attendee list: an occurrence's attendees are the series attendees minus
/// its <c>remove</c> rows plus its <c>add</c> rows (spec 4.1, 4.2). An employee row sets <see cref="EmployeeId"/>,
/// a guest row sets <see cref="Email"/>.
/// </summary>
public class MeetingOccurrenceAttendee : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OccurrenceTaskId { get; set; }
    public string AttendeeKind { get; set; } = MeetingAttendeeKinds.Employee;
    public Guid? EmployeeId { get; set; }
    public string? Email { get; set; }
    public string Change { get; set; } = MeetingAttendeeChanges.Add;
    public string? ResponseStatus { get; set; }
    public string? ResponseReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
