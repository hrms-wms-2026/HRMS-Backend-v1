using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>
/// Work Management monitor alerts (plan W1): the alert goes to the holder of the target's creator position (else the
/// project lead - MonitorAlertRecipients); each escalation goes to the owner of the next Module up that position's
/// ancestor chain who is not already holding it; at the root it is only logged. SLA: 3 days.
/// </summary>
public sealed class WmHierarchyPolicy(
    IMonitorAlertRepository alerts,
    IProjectMonitorSnapshotLoader snapshots,
    IWorkHierarchyService hierarchy,
    IProjectRepository projects,
    CoreEmployeeRepository employees) : IInboxEscalationPolicy
{
    public static readonly TimeSpan EscalationSla = TimeSpan.FromDays(3);
    public const string NoOneAbove = "No Module owner above - logged.";
    public const string NoAlert = "The alert no longer exists - logged.";

    public string Name => "wm_hierarchy";

    public bool AppliesTo(string source, string kindOrType)
        => source == WorkInboxKinds.Source && kindOrType == WorkInboxKinds.MonitorAlert;

    public TimeSpan? Sla => EscalationSla;

    public async Task<EscalationTarget> InitialAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
    {
        var chain = await ChainAsync(tenantId, context.Item.Id, ct);
        if (chain is null)
            return new EscalationTarget.LogOnly(NoAlert);
        return chain.Value.Holder is Guid holder ? await RecipientsAsync(tenantId, [holder], ct) : new EscalationTarget.LogOnly(NoOneAbove);
    }

    public async Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
    {
        var loaded = await ChainAsync(tenantId, context.Item.Id, ct);
        if (loaded is not { } chain)
            return new EscalationTarget.LogOnly(NoAlert);
        var holders = context.CurrentHolderEmployeeIds.Count > 0
            ? context.CurrentHolderEmployeeIds.ToHashSet()
            : chain.Holder is Guid h ? [h] : new HashSet<Guid>();
        // Continue above the highest Module whose owner already holds the alert.
        var from = chain.Owners.Select((owner, index) => (owner, index)).Where(x => holders.Contains(x.owner)).Select(x => x.index + 1).DefaultIfEmpty(0).Max();
        var next = chain.Owners.Skip(from).FirstOrDefault(o => o != Guid.Empty && !holders.Contains(o) && o != context.SubjectEmployeeId);
        return next == Guid.Empty ? new EscalationTarget.LogOnly(NoOneAbove) : await RecipientsAsync(tenantId, [next], ct);
    }

    /// <summary>The alert's current holder and the owners of its creator position's Module chain (that Module first, root last).</summary>
    private async Task<(Guid? Holder, IReadOnlyList<Guid> Owners)?> ChainAsync(Guid tenantId, Guid alertId, CancellationToken ct)
    {
        var alert = await alerts.GetByIdAsync(tenantId, alertId, ct);
        if (alert is null)
            return null;
        var snapshot = await snapshots.LoadAsync(tenantId, alert.ProjectId, ct);
        var tree = await hierarchy.LoadTreeAsync(tenantId, alert.ProjectId, ct);
        var leadId = (await projects.GetByIdForTenantAsync(tenantId, alert.ProjectId, ct))?.LeadId;
        var holder = await MonitorAlertRecipients.ResolveHolderAsync(hierarchy, tenantId, snapshot, tree, leadId, alert.TargetType, alert.TargetId, ct);
        var position = MonitorAlertRecipients.CreatorPosition(alert.TargetType, alert.TargetId, snapshot, tree);
        IReadOnlyList<Guid> owners = position is Guid p ? tree.AncestorChain(p).Select(m => m.OwnerId).ToList() : [];
        return (holder, owners);
    }

    private async Task<EscalationTarget> RecipientsAsync(Guid tenantId, IEnumerable<Guid> employeeIds, CancellationToken ct)
    {
        var recipients = new List<EscalationRecipient>();
        foreach (var employeeId in employeeIds)
            if (await employees.GetByIdAsync(tenantId, employeeId, ct) is { UserId: var userId } && userId != Guid.Empty)
                recipients.Add(new EscalationRecipient(userId, employeeId));
        return recipients.Count == 0 ? new EscalationTarget.LogOnly(NoOneAbove) : new EscalationTarget.Employees(recipients);
    }
}
