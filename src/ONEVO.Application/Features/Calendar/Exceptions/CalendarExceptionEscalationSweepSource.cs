using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>
/// The SLA sweep for calendar meeting-exception cases (plan C2, calendar doc D8): an Open case the reviewer left alone for
/// <see cref="CalendarExceptionDefaults.ManagerSlaDays"/> days goes to the reviewer's own manager; with nobody above, it is
/// only logged - HR is never involved. The case is marked Escalated either way so it is not swept again.
/// </summary>
public sealed class CalendarExceptionEscalationSweepSource(
    IExceptionRepository exceptions,
    IInboxEscalationPolicyRegistry policies,
    INotificationDispatcher notifications,
    ILogger<CalendarExceptionEscalationSweepSource> logger,
    IInboxEscalationRecorder? recorder = null) : IInboxEscalationSweepSource
{
    public const string EscalatedTemplate = "calendar_exception_escalated";

    public string Source => CalendarCaseService.Source;

    public async Task<int> EscalateOverdueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        if (policies.For(Source, nameof(ExceptionType.MeetingNoShow)) is not { } policy)
            return 0;
        var sla = policy.Sla ?? TimeSpan.FromDays(CalendarExceptionDefaults.ManagerSlaDays);
        var overdue = (await exceptions.GetStaleOpenAsync(tenantId, now - sla, ct))
            .Where(e => e.Status == ExceptionStatus.Open && CalendarExceptionTypes.IsCalendar(e.Type) && e.DetectedAt < now - sla)
            .ToList();
        foreach (var exception in overdue)
        {
            var item = new ItemRef(Source, "meeting_exception", exception.Id);
            var context = new EscalationContext(item, exception.Type.ToString(), [], exception.EmployeeId, null, null);
            var holders = await policy.InitialAsync(tenantId, context, ct) is EscalationTarget.Employees initial
                ? initial.Recipients.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value).ToList()
                : [];
            var target = await policy.NextAsync(tenantId, context with { CurrentHolderEmployeeIds = holders }, ct);

            var before = ExceptionStateSnapshot.Of(exception).ToJson();
            exception.Status = ExceptionStatus.Escalated;
            exception.EscalatedAt = now;
            exceptions.Update(exception);

            if (target is EscalationTarget.Employees next)
            {
                var placeholders = new Dictionary<string, string> { ["title"] = exception.Title, ["description"] = exception.Description };
                foreach (var recipient in next.Recipients)
                    await notifications.SendTemplatedAsync(tenantId, recipient.UserId, EscalatedTemplate, placeholders,
                        CalendarCaseService.RelatedEntityType, exception.Id, ct);
            }
            else if (target is EscalationTarget.LogOnly log)
                logger.LogInformation("Calendar exception {ExceptionId} not reviewed in time: {Reason}", exception.Id, log.Reason);

            if (recorder is not null)
                await recorder.RecordAsync(tenantId, new EscalationRecord(item, target, null, "Not reviewed in time", holders, before,
                    ExceptionStateSnapshot.Of(exception).ToJson()), ct);
        }
        if (overdue.Count > 0)
            await exceptions.SaveChangesAsync(ct);
        return overdue.Count;
    }
}
