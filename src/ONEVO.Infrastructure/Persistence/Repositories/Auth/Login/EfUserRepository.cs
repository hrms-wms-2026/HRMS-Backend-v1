using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfUserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted, ct);
        return user;
    }

    public async Task<User?> GetActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == normalizedEmail && u.IsActive && !u.IsDeleted,
            ct);
        return user;
    }

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        return user;
    }

    public async Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<User>();

        return await _db.Users
            .Where(u => ids.Contains(u.Id) && !u.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task<User?> GetByTenantAndEmailAsync(Guid tenantId, string normalizedEmail, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail && !u.IsDeleted,
            ct);
        return user;
    }

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        var addTask = _db.Users.AddAsync(user, ct).AsTask();
        return addTask;
    }
}
