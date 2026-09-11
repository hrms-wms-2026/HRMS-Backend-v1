using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfPasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly ApplicationDbContext _db;

    public EfPasswordResetTokenRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PasswordResetToken?> GetResetTokenByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var resetToken = await _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        return resetToken;
    }

    public async Task<IReadOnlyList<PasswordResetToken>> ListValidByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var resetTokens = await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        return resetTokens;
    }

    public Task AddAsync(PasswordResetToken resetToken, CancellationToken ct = default)
    {
        var addTask = _db.PasswordResetTokens.AddAsync(resetToken, ct).AsTask();
        return addTask;
    }

    public async Task<Guid?> TryConsumeResetTokenAsync(
        string tokenHash, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE password_reset_tokens
            SET used_at = {now}
            WHERE token_hash = {tokenHash}
              AND tenant_id = {tenantId}
              AND used_at IS NULL
              AND expires_at > {now}
            """, ct);

        if (rowsAffected != 1)
            return null;

        var consumedToken = await _db.PasswordResetTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.TenantId == tenantId, ct);

        return consumedToken?.UserId;
    }
}
