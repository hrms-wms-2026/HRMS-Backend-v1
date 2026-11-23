namespace ONEVO.Application.Features.Inbox.Contracts;

/// <summary>One person in an item's approval chain, as the module computed it. Status is an
/// <c>InboxEntryAssigneeStatuses</c> value: "pending" means "must act now".</summary>
public sealed record InboxEntryAssigneeState(Guid EmployeeId, string Status, int SequenceOrder);

/// <summary>What a module says its inbox header should hold for one subject (task 37).</summary>
public sealed record InboxEntryState(
    Guid TenantId, string Source, string Kind, Guid SubjectId, string Category,
    Guid? SubjectEmployeeId, Guid? RequestedByEmployeeId, string Status, string Title, string? Summary,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DecidedAt, Guid? DecidedByEmployeeId,
    IReadOnlyList<InboxEntryAssigneeState> Assignees);

/// <summary>
/// Task 37: the inbox_items header and its assignees. <see cref="StageAsync"/> only stages the change on the current
/// unit of work, so the module's own SaveChanges writes it in the same transaction as the module row.
/// </summary>
public interface IInboxEntryStore
{
    /// <summary>Inserts or updates the header for (tenant, source, kind, subject) and replaces its assignees. Does not save.</summary>
    Task StageAsync(InboxEntryState state, CancellationToken ct);

    /// <summary>The stored header, for the consistency check. Null when there is none.</summary>
    Task<InboxEntryState?> GetAsync(Guid tenantId, string source, string kind, Guid subjectId, CancellationToken ct);

    /// <summary>Items in <paramref name="sources"/> the employee must act on now, counted per (source, kind).</summary>
    Task<IReadOnlyDictionary<(string Source, string Kind), int>> CountPendingForAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<string> sources, CancellationToken ct);
}

/// <summary>Count-only result from one source, the shape task 00a gives every provider. <see cref="ByKind"/> keys are
/// the source-local kind names; the router prefixes them with the source.</summary>
public sealed record InboxSourceCounts(int PendingReceived, int OpenAlerts, IReadOnlyDictionary<string, int> ByKind);

/// <summary>
/// Task 37: a source whose inbox counts come from inbox_items instead of building its rows. The router uses it in place
/// of the source's list for the badge counts. (Task 00a adds CountAsync to every provider; when both are merged, the
/// leave provider's CountAsync is this.)
/// </summary>
public interface IInboxHeaderCountSource
{
    string Source { get; }

    Task<InboxSourceCounts> CountAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);
}
