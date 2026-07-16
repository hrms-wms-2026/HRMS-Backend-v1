namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public sealed record MfaChallengeState(
    Guid UserId,
    Guid TenantId,
    DateTimeOffset ExpiresAt,
    int FailedAttempts);

public interface IMfaChallengeStore
{
    Task<string> CreateAsync(
        Guid userId,
        Guid tenantId,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<MfaChallengeState?> GetAsync(
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
