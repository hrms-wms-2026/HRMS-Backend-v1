using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfUserMfaRepository : IUserMfaRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserMfaRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<UserMfa?> GetTotpAsync(Guid userId, bool isVerified, CancellationToken ct = default)
    {
        var userMfa = await _db.UserMfas.FirstOrDefaultAsync(
            m => m.UserId == userId && m.MethodType == "totp" && m.IsVerified == isVerified,
            ct);
        return userMfa;
    }

    public Task AddAsync(UserMfa mfa, CancellationToken ct = default)
    {
        var addTask = _db.UserMfas.AddAsync(mfa, ct).AsTask();
        return addTask;
    }

    public void Remove(UserMfa mfa)
    {
        _db.UserMfas.Remove(mfa);
    }
}
