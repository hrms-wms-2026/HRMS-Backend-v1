using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformUserInviteRepository
{
    Task AddAsync(PlatformUserInvite invite, CancellationToken ct = default);
    Task<PlatformUserInvite?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<PlatformUserInvite?> GetByPlatformUserIdAsync(Guid platformUserId, CancellationToken ct = default);
    void Update(PlatformUserInvite invite);
}
