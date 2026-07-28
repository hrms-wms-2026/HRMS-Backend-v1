namespace ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;

public sealed record LegalLoginChallengeState(
    Guid TenantId,
    Guid UserId,
    string Origin,
    string CsrfTokenHash,
    DateTimeOffset ExpiresAt);

public sealed record LegalChallengeCommitResult(
    bool Succeeded,
    string? ReplacementRawChallenge = null,
    string? ReplacementRawCsrfToken = null);

/// <summary>
/// Durable, single-use store for the pre-session Legal &amp; Privacy completion challenge
/// (legal_login_challenges). Only the SHA-256 hash of the opaque challenge and of the readable
/// CSRF value are persisted; raw values are returned once to the caller and never logged.
/// </summary>
public interface ILegalLoginChallengeRepository
{
    Task<(string RawChallenge, string RawCsrfToken)> CreateAsync(
        Guid tenantId,
        Guid userId,
        string origin,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<LegalLoginChallengeState?> GetActiveAsync(string rawChallenge, CancellationToken ct = default);

    /// <summary>
    /// Locates an active challenge before a tenant has been resolved. The implementation may use
    /// server-side admin RLS context only for the hash-based lookup and must restore the prior
    /// unresolved context before returning.
    /// </summary>
    Task<LegalLoginChallengeState?> GetActiveForPreTenantContinuationAsync(
        string rawChallenge,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically wins the active challenge, persists all currently staged legal records, and
    /// either consumes the challenge or supersedes it with a replacement.
    /// </summary>
    Task<LegalChallengeCommitResult> TryCommitAcceptancesAsync(
        string rawChallenge,
        LegalLoginChallengeState expectedState,
        bool isComplete,
        TimeSpan replacementLifetime,
        CancellationToken ct = default);
}
