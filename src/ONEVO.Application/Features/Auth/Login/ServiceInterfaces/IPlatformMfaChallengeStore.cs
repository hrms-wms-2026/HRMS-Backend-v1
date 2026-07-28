namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public sealed record PlatformMfaChallengeState(
    Guid PlatformUserId,
    DateTimeOffset ExpiresAt,
    int FailedAttempts);

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
