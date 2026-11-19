using ONEVO.Application.Features.Conversations.Contracts;

namespace ONEVO.Application.Features.Inbox.Escalation;

/// <summary>What a policy needs to decide who reviews an item next. <see cref="KindOrType"/> is the inbox kind, or
/// the module's own type when one kind spans several (monitoring exception types, calendar rules).</summary>
public sealed record EscalationContext(
    ItemRef Item, string KindOrType, IReadOnlyCollection<Guid> CurrentHolderEmployeeIds, Guid? SubjectEmployeeId,
    Guid? LegalEntityId, Guid? ProjectId);

/// <summary>A person the item is routed to: UserId for the bell, EmployeeId for the inbox and the chat (null when
/// the user has no employee record, e.g. a tenant admin - they still get the bell).</summary>
public sealed record EscalationRecipient(Guid UserId, Guid? EmployeeId);

/// <summary>Who an item goes to: some people, or nobody (log only - the end of the chain).</summary>
public abstract record EscalationTarget
{
    public sealed record Employees(IReadOnlyList<EscalationRecipient> Recipients) : EscalationTarget;

    public sealed record LogOnly(string Reason) : EscalationTarget;
}

/// <summary>
/// One routing chain for some (source, kind-or-type)s. <see cref="InitialAsync"/> is who reviews a new item;
/// <see cref="NextAsync"/> is who it goes to after the current holders (manual escalate and the SLA sweep).
/// </summary>
public interface IInboxEscalationPolicy
{
    string Name { get; }

    bool AppliesTo(string source, string kindOrType);

    /// <summary>How long an item may stay open with its holders before the sweep escalates it; null = manual only.</summary>
    TimeSpan? Sla { get; }

    Task<EscalationTarget> InitialAsync(Guid tenantId, EscalationContext context, CancellationToken ct);

    Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext context, CancellationToken ct);
}

/// <summary>The one place escalation policies are looked up (plan README P6).</summary>
public interface IInboxEscalationPolicyRegistry
{
    IReadOnlyList<IInboxEscalationPolicy> All { get; }

    /// <summary>The first registered policy that applies, or null.</summary>
    IInboxEscalationPolicy? For(string source, string kindOrType);
}

public sealed class InboxEscalationPolicyRegistry : IInboxEscalationPolicyRegistry
{
    public InboxEscalationPolicyRegistry(IEnumerable<IInboxEscalationPolicy> policies) => All = policies.ToList();

    public IReadOnlyList<IInboxEscalationPolicy> All { get; }

    public IInboxEscalationPolicy? For(string source, string kindOrType) => All.FirstOrDefault(p => p.AppliesTo(source, kindOrType));
}

/// <summary>One module's half of the SLA sweep: escalates its own overdue open items for one tenant (the tenant's
/// context is already active) through the module's own escalation path, and saves. Returns how many it escalated.</summary>
public interface IInboxEscalationSweepSource
{
    string Source { get; }

    Task<int> EscalateOverdueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct);
}
