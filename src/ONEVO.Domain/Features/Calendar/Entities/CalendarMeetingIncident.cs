using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

/// <summary>How sure an attendance incident is: the provider's own report, or the desktop agent's meeting-app usage.</summary>
public static class CalendarIncidentConfidence
{
    public const string Provider = "provider";
    /// <summary>Shown as "estimated".</summary>
    public const string Agent = "agent";
}

/// <summary>
/// One calendar rule match for one employee and one meeting occurrence (plan C2, calendar doc §6.1). Incidents roll up
/// into a meeting-exception case (<see cref="ExceptionId"/>) when a rule's threshold is reached, or at once for the
/// immediate rules. Evidence never holds application names or activity samples.
/// </summary>
public class CalendarMeetingIncident : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    /// <summary>The meeting (series id for a recurring occurrence); Guid.Empty for a per-window or per-day rule.</summary>
    public Guid EventId { get; set; }
    public DateTimeOffset OccurrenceStart { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string Confidence { get; set; } = CalendarIncidentConfidence.Provider;
    public string EvidenceJson { get; set; } = "{}";
    public Guid? ExceptionId { get; set; }
}
