namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public sealed record MfaChallengeState(
    Guid UserId,
    Guid TenantId,
    string Origin,
    DateTimeOffset ExpiresAt,
    int FailedAttempts)
{
    public MfaChallengeState(
        Guid userId,
        Guid tenantId,
        DateTimeOffset expiresAt,
        int failedAttempts)
        : this(userId, tenantId, "password", expiresAt, failedAttempts)
    {
    }

    /// <summary>Verified external identity to link if the login completes (Google flow only).</summary>
    public PendingExternalIdentity? ExternalIdentity { get; init; }
}

public interface IMfaChallengeStore
{
    Task<string> CreateAsync(
        Guid userId,
        Guid tenantId,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<string> CreateAsync(
        Guid userId,
        Guid tenantId,
        string origin,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<string> CreateAsync(
        Guid userId,
        Guid tenantId,
        string origin,
        PendingExternalIdentity? externalIdentity,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<MfaChallengeState?> GetAsync(
        string challenge,
        CancellationToken ct = default);

    /// <summary>
    /// Locates an active MFA challenge before a tenant has been resolved. The implementation may
    /// use server-side admin RLS context only for the hash-based lookup and must restore the prior
    /// unresolved context before returning.
    /// </summary>
    Task<MfaChallengeState?> GetForPreTenantContinuationAsync(
        string challenge,
        CancellationToken ct = default);

    Task<bool> RegisterFailedAttemptAsync(
        string challenge,
        int maximumAttempts,
        CancellationToken ct = default);

    Task<MfaChallengeState?> TryConsumeAsync(
        string challenge,
        CancellationToken ct = default);
}
