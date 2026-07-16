using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;

/// <summary>
/// EF repository for the canonical Developer Platform access tables:
/// platform_users, platform_user_roles, platform_roles, platform_role_permissions,
/// platform_user_sessions, and platform_auth_events.
/// </summary>
public sealed class EfPlatformAccessRepository :
    IPlatformUserRepository,
    IPlatformUserSessionRepository,
    IPlatformAccessReadRepository,
    IPlatformAuthEventRepository,
    IPlatformRoleRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfPlatformAccessRepository(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    // IPlatformUserRepository

    public Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<PlatformUser?> GetByGoogleSubAsync(string googleSub, CancellationToken ct = default) =>
        _db.PlatformUsers.FirstOrDefaultAsync(u => u.GoogleSub == googleSub, ct);

    public async Task<IReadOnlyList<PlatformUser>> ListUsersAsync(CancellationToken ct = default) =>
        await _db.PlatformUsers.AsNoTracking().ToListAsync(ct);

    public Task AddAsync(PlatformUser user, CancellationToken ct = default) =>
        _db.PlatformUsers.AddAsync(user, ct).AsTask();

    public void UpdateUser(PlatformUser user) =>
        _db.PlatformUsers.Update(user);

    public async Task ReplaceRolesAsync(Guid userId, IEnumerable<Guid> roleIds, CancellationToken ct = default)
    {
        var currentRoles = await _db.PlatformUserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        _db.PlatformUserRoles.RemoveRange(currentRoles);
        var newRoles = roleIds.Select(rId => new PlatformUserRole { UserId = userId, RoleId = rId });
        await _db.PlatformUserRoles.AddRangeAsync(newRoles, ct);
    }

    // IPlatformUserSessionRepository

    Task<PlatformUserSession?> IPlatformUserSessionRepository.GetByIdAsync(Guid sessionId, CancellationToken ct) =>
        _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public Task<PlatformUserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    public Task AddAsync(PlatformUserSession session, CancellationToken ct = default) =>
        _db.PlatformUserSessions.AddAsync(session, ct).AsTask();

    public async Task RevokeByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null && session.RevokedAt is null)
            session.RevokedAt = _clock.UtcNow;
    }

    public async Task RevokeByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var session = await _db.PlatformUserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
        if (session is not null && session.RevokedAt is null)
            session.RevokedAt = _clock.UtcNow;
    }

    // IPlatformRoleRepository

    public async Task<IReadOnlyList<PlatformRole>> ListRolesAsync(CancellationToken ct = default) =>
        await _db.PlatformRoles.AsNoTracking().ToListAsync(ct);

    public Task<PlatformRole?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default) =>
        _db.PlatformRoles.FirstOrDefaultAsync(r => r.Id == roleId, ct);

    public void UpdateRole(PlatformRole role) =>
        _db.PlatformRoles.Update(role);

    public async Task ReplacePermissionsAsync(Guid roleId, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        var currentPerms = await _db.PlatformRolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        _db.PlatformRolePermissions.RemoveRange(currentPerms);
        var newPerms = permissions.Select(p => new PlatformRolePermission { RoleId = roleId, PermissionCode = p });
        await _db.PlatformRolePermissions.AddRangeAsync(newPerms, ct);
    }

    // IPlatformAccessReadRepository

    public Task<List<PlatformUserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
        _db.PlatformUserRoles.AsNoTracking().Where(ur => ur.UserId == userId).ToListAsync(ct);

    public Task<List<PlatformRole>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
        _db.PlatformRoles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToListAsync(ct);

    public Task<List<PlatformRolePermission>> GetRolePermissionsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
        _db.PlatformRolePermissions.AsNoTracking().Where(rp => roleIds.Contains(rp.RoleId)).ToListAsync(ct);

    public async Task RevokeAllByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await _db.PlatformUserSessions.Where(s => s.AccountId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.RevokedAt = _clock.UtcNow;
        }
    }

    public async Task<IReadOnlyList<PlatformUserSession>> ListByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _db.PlatformUserSessions.AsNoTracking().Where(s => s.AccountId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

    // IPlatformAuthEventRepository

    async Task<IReadOnlyList<PlatformAuthEvent>> IPlatformAuthEventRepository.ListByUserIdAsync(Guid userId, CancellationToken ct) =>
        await _db.PlatformAuthEvents.AsNoTracking().Where(e => e.UserId == userId).OrderByDescending(e => e.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<PlatformAuthEvent>> ListAllAsync(CancellationToken ct = default) =>
        await _db.PlatformAuthEvents.AsNoTracking().OrderByDescending(e => e.CreatedAt).ToListAsync(ct);

    public Task AddAsync(PlatformAuthEvent authEvent, CancellationToken ct = default) =>
        _db.PlatformAuthEvents.AddAsync(authEvent, ct).AsTask();
}
