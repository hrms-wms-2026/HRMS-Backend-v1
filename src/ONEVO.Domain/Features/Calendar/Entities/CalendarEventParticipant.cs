using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventParticipantStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string ResolutionRequested = "resolution_requested";
    public const string ReplacementNominated = "replacement_nominated";
}

public class CalendarEventParticipant : BaseEntity
{
    public Guid EventId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ResponseStatus { get; set; } = CalendarEventParticipantStatuses.Pending;
    public string? ResponseReason { get; set; }
}
