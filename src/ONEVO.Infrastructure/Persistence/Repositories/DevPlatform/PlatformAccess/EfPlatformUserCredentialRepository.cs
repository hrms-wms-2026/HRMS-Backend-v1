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

    public async Task<Guid?> TryConsumeResetTokenAsync(
        string tokenHash, DateTimeOffset now, CancellationToken ct = default)
    {
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE platform_user_credentials
            SET reset_token_expires_at = NULL,
                updated_at = {now}
            WHERE reset_token_hash = {tokenHash}
              AND reset_token_expires_at > {now}
            """, ct);

        if (rowsAffected != 1)
            return null;

        var consumed = await _db.PlatformUserCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ResetTokenHash == tokenHash, ct);

        return consumed?.PlatformUserId;
    }
}
