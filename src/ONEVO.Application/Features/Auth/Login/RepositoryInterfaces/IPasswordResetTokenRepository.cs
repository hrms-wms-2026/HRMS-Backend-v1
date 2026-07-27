using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> GetResetTokenByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<PasswordResetToken>> ListValidByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task AddAsync(PasswordResetToken resetToken, CancellationToken ct = default);

    /// <summary>
    /// Atomically marks one unused, unexpired token for the given tenant as used and returns the
    /// owning user's id, or null if no such token exists (already used, expired, wrong tenant, or
    /// unknown hash). Backed by a single parameterized UPDATE ... WHERE used_at IS NULL guard, so
    /// under concurrent calls with the same token exactly one caller ever gets a non-null result.
    /// </summary>
    Task<Guid?> TryConsumeResetTokenAsync(
        string tokenHash,
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken ct = default);
}
