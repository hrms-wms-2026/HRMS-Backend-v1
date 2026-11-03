using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// Scoped IOutboxDispatchContext. OutboxProcessor calls Begin before invoking a message's handler
/// and End afterwards; everything the handler resolves from the same scope (e.g.
/// TransactionalEmailService) sees the current message. Keys look like "outbox/{id:N}/{n}"
/// (n = 1-based order of the send within the handler): 47 characters, well inside Resend's
/// 1-256 character limit.
/// </summary>
public sealed class OutboxDispatchContext : IOutboxDispatchContext
{
    private int _sends;

    public Guid? CurrentMessageId { get; private set; }

    public void Begin(Guid messageId)
    {
        CurrentMessageId = messageId;
        _sends = 0;
    }

    public void End()
    {
        CurrentMessageId = null;
        _sends = 0;
    }

    public string? NextIdempotencyKey()
    {
        if (CurrentMessageId is not { } messageId)
            return null;

        _sends++;
        return $"outbox/{messageId:N}/{_sends}";
    }
}
