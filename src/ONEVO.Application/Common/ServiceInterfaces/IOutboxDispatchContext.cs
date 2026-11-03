namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Scope-lived information about the outbox message the OutboxProcessor is dispatching right now,
/// so outbound integrations can pass a provider idempotency key without any handler changing.
/// Outside an outbox dispatch (e.g. a request-time send) there is no message and no key.
/// </summary>
public interface IOutboxDispatchContext
{
    /// <summary>The outbox message being dispatched, or null outside a dispatch.</summary>
    Guid? CurrentMessageId { get; }

    /// <summary>
    /// The idempotency key for the next outbound send made while dispatching the current message,
    /// or null outside a dispatch. Keys are derived from the message id and the send's order within
    /// the handler, so a retry of the same message (same handler, same payload) reproduces the same
    /// keys. Only providers that genuinely honour a key make that retry safe; see the email adapters.
    /// </summary>
    string? NextIdempotencyKey();
}
