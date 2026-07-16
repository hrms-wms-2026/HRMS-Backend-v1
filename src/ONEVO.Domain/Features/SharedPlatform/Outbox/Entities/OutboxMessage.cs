namespace ONEVO.Domain.Features.SharedPlatform.Entities;

/// <summary>
/// Transactional outbox record. The business change and its outbox message are
/// saved in the same transaction; a background worker publishes pending messages
/// and retries failures with backoff.
/// </summary>
public class OutboxMessage
{
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
}

public static class OutboxMessageStatus
{
    public const string Pending = "pending";
    public const string Published = "published";
    public const string Failed = "failed";
}
