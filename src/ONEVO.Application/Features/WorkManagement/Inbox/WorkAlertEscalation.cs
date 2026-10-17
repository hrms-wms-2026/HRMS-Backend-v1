using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>
/// Escalating a Work monitor alert the standard way (plan W1): the next holder comes from <see cref="WmHierarchyPolicy"/>,
/// the escalation is recorded by the shared recorder (journal, the new holder joins the chat as a viewer, a system
/// message) and the new holder gets a bell. Used by the manual "escalate" action and by the 3-day SLA sweep.
/// </summary>
public sealed class WorkAlertEscalation(
    IInboxEscalationPolicyRegistry policies,
    IInboxEscalationRecorder recorder,
    IInboxDecisionJournal journal,
    INotificationDispatcher notifications)
{
    public const string EscalatedTemplate = "work_monitor_alert_escalated";

    public static ItemRef Ref(Guid alertId) => new(WorkInboxKinds.Source, WorkInboxKinds.MonitorAlert, alertId);

    /// <summary>Who holds the alert now: the last escalation's targets, else the policy's first holder.</summary>
    public async Task<IReadOnlyCollection<Guid>> CurrentHoldersAsync(Guid tenantId, MonitorAlert alert, CancellationToken ct)
    {
        var last = (await journal.ListForItemAsync(tenantId, Ref(alert.Id), ct))
            .Where(e => e.Action == InboxJournalActions.Escalate).OrderBy(e => e.DecidedAt).LastOrDefault();
        if (last is not null && EscalatedTo(last) is { Count: > 0 } targets)
            return targets;
        return Policy() is { } policy && await policy.InitialAsync(tenantId, Context(alert, []), ct) is EscalationTarget.Employees first
            ? first.Recipients.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value).ToList()
            : [];
    }

    /// <summary>The latest escalation journal entry of the alert, if any.</summary>
    public async Task<InboxDecisionJournalEntry?> LastEscalationAsync(Guid tenantId, Guid alertId, CancellationToken ct)
        => (await journal.ListForItemAsync(tenantId, Ref(alertId), ct))
            .Where(e => e.Action == InboxJournalActions.Escalate).OrderBy(e => e.DecidedAt).LastOrDefault();

    public async Task<IReadOnlyCollection<Guid>> EscalateAsync(Guid tenantId, MonitorAlert alert, Guid? actorEmployeeId, string? note, CancellationToken ct)
    {
        var holders = await CurrentHoldersAsync(tenantId, alert, ct);
        var target = Policy() is { } policy
            ? await policy.NextAsync(tenantId, Context(alert, holders), ct)
            : new EscalationTarget.LogOnly("No escalation policy for Work alerts.");
        // The dispatcher only adds the bell; queue it before the recorder's save so both commit together.
        if (target is EscalationTarget.Employees next)
            foreach (var recipient in next.Recipients)
                await notifications.SendTemplatedAsync(tenantId, recipient.UserId, EscalatedTemplate,
                    new Dictionary<string, string> { ["title"] = alert.TargetTitle, ["message"] = alert.Message },
                    InboxNotificationNavigation.RelatedEntityType(WorkInboxKinds.Source, WorkInboxKinds.MonitorAlert), alert.Id, ct);
        return await recorder.RecordAsync(tenantId, new EscalationRecord(Ref(alert.Id), target, actorEmployeeId, note, holders), ct);
    }

    private IInboxEscalationPolicy? Policy() => policies.For(WorkInboxKinds.Source, WorkInboxKinds.MonitorAlert);

    private static EscalationContext Context(MonitorAlert alert, IReadOnlyCollection<Guid> holders)
        => new(Ref(alert.Id), WorkInboxKinds.MonitorAlert, holders, alert.SubjectEmployeeId, null, alert.ProjectId);

    public static IReadOnlyList<Guid> EscalatedTo(InboxDecisionJournalEntry entry) => InboxEscalationJournal.EscalatedTo(entry);

    public static bool LoggedOnly(InboxDecisionJournalEntry entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(entry.AfterJson ?? "{}");
            return doc.RootElement.TryGetProperty("loggedOnly", out var reason) && reason.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The 3-day SLA sweep for Work monitor alerts (plan W1): an open alert nobody escalated for 3 days goes one step
/// up the Module chain; once the root is reached and the escalation was only logged, it is left alone.</summary>
public sealed class WorkMonitorAlertSweepSource(IMonitorAlertRepository alerts, WorkAlertEscalation escalation) : IInboxEscalationSweepSource
{
    public string Source => WorkInboxKinds.Source;

    public async Task<int> EscalateOverdueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var due = now - WmHierarchyPolicy.EscalationSla;
        var escalated = 0;
        foreach (var alert in await alerts.ListOpenDetectedBeforeAsync(tenantId, due, ct))
        {
            var last = await escalation.LastEscalationAsync(tenantId, alert.Id, ct);
            if (last is not null && (last.DecidedAt > due || WorkAlertEscalation.LoggedOnly(last)))
                continue;
            await escalation.EscalateAsync(tenantId, alert, null, "Not handled in time", ct);
            escalated++;
        }
        return escalated;
    }
}
