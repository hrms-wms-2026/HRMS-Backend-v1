namespace ONEVO.Application.Common.ServiceInterfaces;

public enum IdempotencyOutcome
{
    /// <summary>No prior record; an in-progress record was created and the request may proceed.</summary>
    Started,

    /// <summary>Same key + same body already completed; replay the stored response.</summary>
    Replay,

    /// <summary>Same key but a different request body; must be rejected with 409.</summary>
    HashMismatch,

    /// <summary>Same key is still being processed by another request; reject with 409.</summary>
    InFlight
}

public sealed record IdempotencyBeginResult(
    IdempotencyOutcome Outcome,
    Guid RecordId,
    int? ResponseStatusCode = null,
    string? ResponseBody = null);

/// <summary>
/// Persistence for Idempotency-Key request deduplication. Begin must commit the
/// in-progress marker immediately (its own SaveChanges) so concurrent duplicates
/// are detected via the unique (key, scope, requester) constraint.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyBeginResult> TryBeginAsync(
        string key,
        string scope,
        string requesterId,
        string requestHash,
        TimeSpan ttl,
        CancellationToken ct = default);

    /// <summary>Stores the response so later duplicates replay it.</summary>
    Task CompleteAsync(Guid recordId, int statusCode, string? responseBody, CancellationToken ct = default);

    /// <summary>Removes the marker (e.g. after a 5xx) so the client may retry.</summary>
    Task AbandonAsync(Guid recordId, CancellationToken ct = default);

    /// <summary>Deletes expired records; called periodically by the outbox worker.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}
