using ONEVO.Application.Features.Inbox.Escalation;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>
/// Work Management monitor alerts: creator-position holder, then the next ancestor Module owner, root = log only.
/// Registered now so the registry knows the kind; the chain itself arrives with the WM alignment (plan part W1) -
/// until then every escalation is logged only and there is no SLA.
/// </summary>
public sealed class WmHierarchyPolicy : IInboxEscalationPolicy
{
    public const string NotYetRouted = "Work Management escalation is not routed yet - logged.";

    public string Name => "wm_hierarchy";

    public bool AppliesTo(string source, string kindOrType)
        => source == WorkInboxKinds.Source && kindOrType == WorkInboxKinds.MonitorAlert;

    public TimeSpan? Sla => null;

    public Task<EscalationTarget> InitialAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
        => Task.FromResult<EscalationTarget>(new EscalationTarget.LogOnly(NotYetRouted));

    public Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
        => Task.FromResult<EscalationTarget>(new EscalationTarget.LogOnly(NotYetRouted));
}
