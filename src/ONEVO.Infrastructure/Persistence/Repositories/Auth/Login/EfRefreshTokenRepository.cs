using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _db;

    public EfRefreshTokenRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var refreshToken = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        return refreshToken;
    }

    public async Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var refreshTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        return refreshTokens;
    }

    public Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        var addTask = _db.RefreshTokens.AddAsync(refreshToken, ct).AsTask();
        return addTask;
    }
}
