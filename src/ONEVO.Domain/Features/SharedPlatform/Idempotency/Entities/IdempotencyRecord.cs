namespace ONEVO.Domain.Features.SharedPlatform.Entities;

/// <summary>
/// Stored outcome of an idempotent request. Uniqueness is (Key, Scope, RequesterId):
/// replaying the same key with the same body returns the stored response; the same
/// key with a different body is rejected with 409.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; }

    /// <summary>Client-generated Idempotency-Key header value.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Endpoint scope, e.g. "POST:/admin/v1/tenants".</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Authenticated caller id (admin/user id) so keys cannot collide across callers.</summary>
    public string RequesterId { get; set; } = string.Empty;

    /// <summary>SHA-256 of the request payload; detects key reuse with a different body.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public string Status { get; set; } = IdempotencyRecordStatus.InProgress;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class IdempotencyRecordStatus
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}
