namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Enqueues an outbox message into the current unit of work. The message is not
/// persisted until the caller commits via IUnitOfWork.SaveChangesAsync, which keeps
/// the business change and its side effect in one transaction.
/// </summary>
public interface IOutboxWriter
{
    Task EnqueueAsync<TPayload>(
        string type,
        TPayload payload,
        Guid? tenantId = null,
        CancellationToken ct = default);

    /// <summary>Inserts the message immediately with INSERT … ON CONFLICT (deduplication_key) DO
    /// NOTHING, inside the caller's open transaction (IUnitOfWork.ExecuteInTransactionAsync).
    /// Returns false when a message with this key already exists - a silent no-op that never fails
    /// the caller's save. Throws InvalidOperationException when no transaction is active, so a keyed
    /// message can never commit separately from its business change.</summary>
    Task<bool> EnqueueKeyedAsync<TPayload>(
        string type,
        TPayload payload,
        string deduplicationKey,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
