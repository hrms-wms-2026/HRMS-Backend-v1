using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// The stale-case sweep for monitoring exception cases, moved out of ExceptionDetectionJob unchanged: an Open case
/// older than its policy's SLA (3 days) becomes Escalated with EscalatedById null, HR gets the "escalated" alert
/// through the tenant's alert router, and everything is saved together. A failure to alert never blocks the save.
/// </summary>
public sealed class ExceptionEscalationSweepSource : IInboxEscalationSweepSource
{
    private readonly IExceptionRepository _exceptions;
    private readonly IExceptionAlertRouterFactory _routers;
    private readonly IInboxEscalationPolicyRegistry _policies;
    private readonly ILogger<ExceptionEscalationSweepSource> _logger;
    private readonly IInboxEscalationRecorder? _recorder;

    public ExceptionEscalationSweepSource(
        IExceptionRepository exceptions, IExceptionAlertRouterFactory routers, IInboxEscalationPolicyRegistry policies,
        ILogger<ExceptionEscalationSweepSource> logger, IInboxEscalationRecorder? recorder = null)
    {
        _exceptions = exceptions;
        _routers = routers;
        _policies = policies;
        _logger = logger;
        _recorder = recorder;
    }

    public string Source => ReportingLineThenHrPolicy.Source;

    public async Task<int> EscalateOverdueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var slas = Enum.GetValues<ExceptionType>()
            .Select(type => (type, sla: _policies.For(Source, type.ToString())?.Sla))
            .Where(x => x.sla is not null)
            .ToDictionary(x => x.type, x => x.sla!.Value);
        if (slas.Count == 0)
            return 0;

        var candidates = await _exceptions.GetStaleOpenAsync(tenantId, now - slas.Values.Min(), ct);
        // Calendar meeting exceptions escalate through the calendar policy (log only at the top), never to HR.
        var overdue = candidates.Where(e => e.Status == ExceptionStatus.Open && !CalendarExceptionTypes.IsCalendar(e.Type)
            && slas.TryGetValue(e.Type, out var sla) && e.DetectedAt < now - sla).ToList();
        if (overdue.Count == 0)
            return 0;

        var alerts = _routers.CreateForTenant(tenantId);
        foreach (var exception in overdue)
        {
            var before = ExceptionStateSnapshot.Of(exception).ToJson();
            exception.Status = ExceptionStatus.Escalated;
            exception.EscalatedAt = now;
            _exceptions.Update(exception);
            await NotifySafelyAsync(() => alerts.NotifyEscalatedAsync(exception, ct), exception, ct);
            await RecordSafelyAsync(tenantId, exception, before, ct);
        }
        await _exceptions.SaveChangesAsync(ct);
        return overdue.Count;
    }

    /// <summary>The automatic escalation in the inbox the standard way (journal, HR joins the chat, a system line).</summary>
    private async Task RecordSafelyAsync(Guid tenantId, MonitoringException exception, string before, CancellationToken ct)
    {
        if (_recorder is null)
            return;
        var item = new ONEVO.Application.Features.Conversations.Contracts.ItemRef(ExceptionInboxKinds.Source, ExceptionInboxKinds.Case, exception.Id);
        await NotifySafelyAsync(async () =>
        {
            var target = _policies.For(Source, exception.Type.ToString()) is { } policy
                ? await policy.NextAsync(tenantId, new EscalationContext(item, exception.Type.ToString(), [], exception.EmployeeId, null, null), ct)
                : new EscalationTarget.LogOnly("No escalation policy for this case type.");
            await _recorder.RecordAsync(tenantId, new EscalationRecord(item, target, null, "Not reviewed in time", [], before,
                ExceptionStateSnapshot.Of(exception).ToJson()), ct);
        }, exception, ct);
    }

    private async Task NotifySafelyAsync(Func<Task> notify, MonitoringException exception, CancellationToken ct)
    {
        try
        {
            await notify();
        }
        catch (System.Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Could not send the escalation alert for exception {ExceptionId} ({Type}) of employee {EmployeeId}; the escalation is still saved.",
                exception.Id, exception.Type, exception.EmployeeId);
        }
    }
}
