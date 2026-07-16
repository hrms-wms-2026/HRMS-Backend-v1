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
}
