namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <param name="PendingGoogleSub">
/// Set only for a Google sign-in that matched a platform user by email and has no Google binding
/// yet. The binding is written only after this challenge's TOTP code is verified, so a Google
/// identity is never linked to an admin account on the strength of an email address alone.
/// </param>
public sealed record PlatformMfaChallengeState(
    Guid PlatformUserId,
    DateTimeOffset ExpiresAt,
    int FailedAttempts,
    string? PendingGoogleSub = null);

/// <summary>
/// Admin/platform equivalent of <see cref="IMfaChallengeStore"/>. Kept as a separate interface
/// rather than reusing the tenant-scoped one because platform users are not tenant-owned — there
/// is no TenantId to carry, and admin/tenant auth concerns are kept structurally separate
/// throughout this codebase (AdminScheme vs TenantScheme, admin_session vs onevo_session, etc).
/// </summary>
public interface IPlatformMfaChallengeStore
{
    Task<string> CreateAsync(
        Guid platformUserId,
        TimeSpan lifetime,
        CancellationToken ct = default);

    /// <summary>Creates a challenge that, once its TOTP code is verified, binds
    /// <paramref name="googleSub"/> to the platform user.</summary>
    Task<string> CreateForGoogleLinkAsync(
        Guid platformUserId,
        string googleSub,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<PlatformMfaChallengeState?> GetAsync(
        string challenge,
        CancellationToken ct = default);

    Task<bool> RegisterFailedAttemptAsync(
        string challenge,
        int maximumAttempts,
        CancellationToken ct = default);

    Task<PlatformMfaChallengeState?> TryConsumeAsync(
        string challenge,
        CancellationToken ct = default);
}
