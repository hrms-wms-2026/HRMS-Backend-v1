using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformUserCredentialRepository
{
    Task<PlatformUserCredential?> GetActivePasswordCredentialAsync(
        Guid platformUserId,
        CancellationToken ct = default);

    Task AddAsync(PlatformUserCredential credential, CancellationToken ct = default);
    void Update(PlatformUserCredential credential);
}
