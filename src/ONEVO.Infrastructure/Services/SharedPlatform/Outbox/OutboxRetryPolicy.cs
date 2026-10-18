using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>Next state of a message whose attempt failed.</summary>
public sealed record OutboxFailureOutcome(
    string Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? ProcessedAt,
    string LastError);

/// <summary>
/// The outbox retry rule, unchanged from before claiming was introduced: a failed attempt returns
/// the message to Pending with exponential backoff (30 s, 60 s, 120 s ... capped at 1 h) and the
/// MaxAttempts-th failure parks it as Failed.
/// </summary>
public static class OutboxRetryPolicy
{
    public const int MaxAttempts = 8;
    public const int LastErrorMaxLength = 2000;
    public static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public static OutboxFailureOutcome OnFailure(int attemptCountBefore, Exception error, DateTimeOffset now)
    {
        var attempts = attemptCountBefore + 1;
        var lastError = error.Message.Length <= LastErrorMaxLength ? error.Message : error.Message[..LastErrorMaxLength];

        if (attempts >= MaxAttempts)
            return new OutboxFailureOutcome(OutboxMessageStatus.Failed, attempts, NextAttemptAt: null, ProcessedAt: now, lastError);

        var delaySeconds = BaseRetryDelay.TotalSeconds * Math.Pow(2, attempts - 1);
        var delay = TimeSpan.FromSeconds(Math.Min(delaySeconds, MaxRetryDelay.TotalSeconds));
        return new OutboxFailureOutcome(OutboxMessageStatus.Pending, attempts, now.Add(delay), ProcessedAt: null, lastError);
    }

    /// <summary>
    /// Reclaiming a message whose owner's lease expired counts the abandoned attempt (see
    /// OutboxClaimStore.ClaimBatchAsync). A message that has used up MaxAttempts that way - e.g. one
    /// that takes its instance down every time - is parked instead of dispatched again, so the
    /// max-attempts rule bounds crash loops exactly as it bounds ordinary failures.
    /// </summary>
    public static bool ShouldPark(int attemptCount) => attemptCount >= MaxAttempts;

    public static OutboxFailureOutcome Park(int attemptCount, string lastError, DateTimeOffset now)
        => new(OutboxMessageStatus.Failed, attemptCount, NextAttemptAt: null, ProcessedAt: now, lastError);
}
