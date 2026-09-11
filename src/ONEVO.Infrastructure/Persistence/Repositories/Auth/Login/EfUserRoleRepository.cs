using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfUserRoleRepository : IUserRoleRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserRoleRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserRole>> ListActiveByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now));

        var userRoles = await query.ToListAsync(ct);
        return userRoles;
    }

    public async Task<IReadOnlyList<Guid>> ListUserIdsByRoleAsync(
        Guid roleId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == roleId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Select(ur => ur.UserId)
            .Distinct();

        var userIds = await query.ToListAsync(ct);
        return userIds;
    }

    public Task AddAsync(UserRole userRole, CancellationToken ct = default)
    {
        var addTask = _db.UserRoles.AddAsync(userRole, ct).AsTask();
        return addTask;
    }
}
