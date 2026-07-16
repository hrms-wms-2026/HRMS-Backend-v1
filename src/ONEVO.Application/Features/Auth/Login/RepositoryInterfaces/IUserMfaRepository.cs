using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IUserMfaRepository
{
    Task<UserMfa?> GetTotpAsync(Guid userId, bool isVerified, CancellationToken ct = default);
    Task AddAsync(UserMfa mfa, CancellationToken ct = default);
    void Remove(UserMfa mfa);
}
