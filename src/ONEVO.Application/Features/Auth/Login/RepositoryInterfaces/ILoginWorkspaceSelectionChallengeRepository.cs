namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public sealed record WorkspaceCandidateSnapshot(
    Guid TenantId,
    Guid UserId,
    string Slug,
    string DisplayName);

public sealed record LoginWorkspaceSelectionChallengeState(
    string NormalizedEmail,
    string Origin,
    IReadOnlyList<WorkspaceCandidateSnapshot> Candidates,
    DateTimeOffset ExpiresAt,
    int FailedAttemptCount)
{
    public LoginWorkspaceSelectionChallengeState(
        string normalizedEmail,
        IReadOnlyList<WorkspaceCandidateSnapshot> candidates,
        DateTimeOffset expiresAt,
        int failedAttemptCount)
        : this(normalizedEmail, "password", candidates, expiresAt, failedAttemptCount)
    {
    }
}

/// <summary>
/// Durable, single-use store for base-domain multi-match workspace selection challenges.
/// Only the SHA-256 hash of the opaque challenge is persisted; the raw value is returned once
/// to the caller and is never stored or logged. Mirrors IMfaChallengeStore's atomic-update
/// contract for failed_attempt_count and single-use consumption.
/// </summary>
public interface ILoginWorkspaceSelectionChallengeRepository
{
    Task<string> CreateAsync(
        string normalizedEmail,
        IReadOnlyList<WorkspaceCandidateSnapshot> candidates,
        string? ipAddress,
        string? userAgent,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<string> CreateAsync(
        string normalizedEmail,
        string origin,
        IReadOnlyList<WorkspaceCandidateSnapshot> candidates,
        string? ipAddress,
        string? userAgent,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<LoginWorkspaceSelectionChallengeState?> GetActiveAsync(
        string rawChallenge,
        CancellationToken ct = default);

    Task<bool> RegisterFailedAttemptAsync(
        string rawChallenge,
        int maximumAttempts,
        CancellationToken ct = default);

    Task<LoginWorkspaceSelectionChallengeState?> TryConsumeAsync(
        string rawChallenge,
        CancellationToken ct = default);
}
