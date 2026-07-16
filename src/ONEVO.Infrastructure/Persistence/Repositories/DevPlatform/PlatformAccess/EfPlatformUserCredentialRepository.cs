using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

public sealed class EfPlatformUserCredentialRepository : IPlatformUserCredentialRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformUserCredentialRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PlatformUserCredential?> GetActivePasswordCredentialAsync(
        Guid platformUserId,
        CancellationToken ct = default)
    {
        return _db.PlatformUserCredentials.FirstOrDefaultAsync(
            credential =>
                credential.PlatformUserId == platformUserId &&
                credential.CredentialType == PlatformUserCredential.PasswordType &&
                credential.RevokedAt == null,
            ct);
    }

    public Task AddAsync(PlatformUserCredential credential, CancellationToken ct = default)
    {
        return _db.PlatformUserCredentials.AddAsync(credential, ct).AsTask();
    }

    public void Update(PlatformUserCredential credential)
    {
        _db.PlatformUserCredentials.Update(credential);
    }
}
