namespace ONEVO.Domain.Features.SharedPlatform.Entities;

/// <summary>
/// Transactional outbox record. The business change and its outbox message are
/// saved in the same transaction; a background worker publishes pending messages
/// and retries failures with backoff.
///
/// Every API instance runs that worker, so a message is first claimed atomically in the database
/// (status Processing, LockedBy = the claiming instance, LockedUntil = its lease) and only the
/// owner may complete it. Delivery guarantee: at-least-once with no concurrent double-processing.
/// It is NOT exactly-once: a crash after a provider accepted a send but before the row is marked
/// Published retries the message once its lease expires, which may deliver a duplicate.
/// </summary>
public class OutboxMessage
{
    public const int LockedByMaxLength = 200;

    public Guid Id { get; set; }

    /// <summary>Message type key used to route to a handler (see OutboxMessageTypes).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// AES-encrypted JSON payload. Encrypted because payloads may carry one-time
    /// tokens or personal data that must not sit in plaintext at rest.
    /// </summary>
    public string EncryptedPayload { get; set; } = string.Empty;

    /// <summary>Informational tenant reference; the outbox itself is platform-scoped (no RLS).</summary>
    public Guid? TenantId { get; set; }

    public string Status { get; set; } = OutboxMessageStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>Optional idempotency key (unique when set). Keyed messages are inserted with
    /// INSERT … ON CONFLICT DO NOTHING inside the caller's transaction, so the same logical message
    /// (e.g. one email per recipient per operation) is queued at most once.</summary>
    public string? DeduplicationKey { get; set; }
    /// <summary>Owner id of the processor instance holding the claim (null unless Processing).</summary>
    public string? LockedBy { get; set; }

    /// <summary>End of the owner's lease. Once it passes, another instance may reclaim the message.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>When the current claimed attempt's handler started; null from the claim until the lease is
    /// renewed right before the handler runs, so a reclaim counts only attempts that actually ran.</summary>
    public DateTimeOffset? AttemptStartedAt { get; set; }
}

public static class OutboxMessageStatus
{
    public const string Pending = "pending";
    /// <summary>Claimed by one processor instance (see OutboxMessage.LockedBy / LockedUntil).</summary>
    public const string Processing = "processing";
    public const string Published = "published";
    public const string Failed = "failed";
}
