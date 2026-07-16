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
}
