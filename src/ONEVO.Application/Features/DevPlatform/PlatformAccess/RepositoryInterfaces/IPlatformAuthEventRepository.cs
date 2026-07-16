using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformAuthEventRepository
{
    Task<IReadOnlyList<PlatformAuthEvent>> ListByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<PlatformAuthEvent>> ListAllAsync(CancellationToken ct = default);
    Task AddAsync(PlatformAuthEvent authEvent, CancellationToken ct = default);
}
