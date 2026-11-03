using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>What every meeting source needs to describe one employee's meetings to the C2 rules.</summary>
/// <param name="Identities">The employee's work email and every connected calendar account's email (attendance identity).</param>
/// <param name="ReportingManagerId">The employee's reporting manager, when there is one.</param>
/// <param name="Agent">Desktop-agent usage ratio by (event id, occurrence start); empty when activity monitoring is off.</param>
public sealed record EmployeeOccurrenceContext(Guid TenantId, Employee Employee, DateTimeOffset From, DateTimeOffset To,
    IReadOnlySet<string> Identities, Guid? ReportingManagerId, IReadOnlyDictionary<(Guid, DateTimeOffset), decimal> Agent, DateTimeOffset Now);

/// <summary>
/// A kind of meeting the C2 engine evaluates (calendar events, task meetings). Each lists the employee's occurrences in
/// [From, To] that they organize or attend, as <see cref="CalendarMeetingView"/>s; <see cref="CalendarMeetingView.EventId"/>
/// plus <see cref="CalendarMeetingView.Start"/> must identify the occurrence stably (the incident key).
/// </summary>
public interface IEmployeeMeetingOccurrenceSource
{
    Task<IReadOnlyList<CalendarMeetingView>> ListAsync(EmployeeOccurrenceContext context, CancellationToken ct);
}
