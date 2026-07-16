using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfAuthRepository :
    IUserRepository,
    IRefreshTokenRepository,
    ISessionRepository,
    IPasswordResetTokenRepository,
    IUserMfaRepository,
    IRoleRepository,
    IRolePermissionRepository,
    IPermissionRepository,
    IUserPermissionOverrideRepository,
    IUserRoleRepository,
    IFeatureAccessGrantRepository,
    IAuditLogRepository,
    IRoleTemplateRepository
{
    private readonly ApplicationDbContext _db;

    public EfAuthRepository(ApplicationDbContext db) => _db = db;

    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail && !u.IsDeleted, ct);

    public Task<User?> GetActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive && !u.IsDeleted, ct);

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);

    public Task<User?> GetByTenantAndEmailAsync(Guid tenantId, string normalizedEmail, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.Email == normalizedEmail && !u.IsDeleted, ct);

    public Task AddAsync(User user, CancellationToken ct = default) =>
        _db.Users.AddAsync(user, ct).AsTask();

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

    public Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default) =>
        _db.RefreshTokens.AddAsync(refreshToken, ct).AsTask();

    public Task<Session?> GetLatestActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.Sessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);

    Task<Session?> ISessionRepository.GetByIdAsync(Guid sessionId, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    Task<Session?> ISessionRepository.GetByKeyHashAsync(string keyHash, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);

    async Task ISessionRepository.RevokeByKeyHashAsync(string keyHash, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
        if (session is not null)
            session.IsRevoked = true;
        // Caller must call IUnitOfWork.SaveChangesAsync
    }

    async Task ISessionRepository.RevokeByIdAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null)
            session.IsRevoked = true;
        // Caller must call IUnitOfWork.SaveChangesAsync
    }

    public Task AddAsync(Session session, CancellationToken ct = default) =>
        _db.Sessions.AddAsync(session, ct).AsTask();

    public Task<PasswordResetToken?> GetResetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<PasswordResetToken>> ListValidByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

    public Task AddAsync(PasswordResetToken resetToken, CancellationToken ct = default) =>
        _db.PasswordResetTokens.AddAsync(resetToken, ct).AsTask();

    public Task<UserMfa?> GetTotpAsync(Guid userId, bool isVerified, CancellationToken ct = default) =>
        _db.UserMfas.FirstOrDefaultAsync(
            m => m.UserId == userId && m.MethodType == "totp" && m.IsVerified == isVerified,
            ct);

    public Task AddAsync(UserMfa mfa, CancellationToken ct = default) =>
        _db.UserMfas.AddAsync(mfa, ct).AsTask();

    public void Remove(UserMfa mfa) => _db.UserMfas.Remove(mfa);

    public Task<Role?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);

    public Task<Role?> GetByIdForTenantAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);

    public Task<Role?> GetByNameForTenantAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == name, ct);

    public Task<Role?> GetBySourceTemplateForTenantAsync(Guid tenantId, Guid templateId, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.SourceTemplateId == templateId,
            ct);

    public async Task<IReadOnlyList<Role>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.Roles
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public Task AddAsync(Role role, CancellationToken ct = default) =>
        _db.Roles.AddAsync(role, ct).AsTask();

    public void Remove(Role role) => _db.Roles.Remove(role);

    public async Task<IReadOnlyList<RolePermission>> ListByRoleAsync(Guid roleId, CancellationToken ct = default) =>
        await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);

    public Task AddRangeAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken ct = default) =>
        _db.RolePermissions.AddRangeAsync(rolePermissions, ct);

    public void RemoveRange(IEnumerable<RolePermission> rolePermissions) =>
        _db.RolePermissions.RemoveRange(rolePermissions);

    public Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);

    public async Task<IReadOnlyList<Permission>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<Permission>();

        return await _db.Permissions
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default)
    {
        var list = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (list.Count == 0)
            return Array.Empty<Permission>();

        return await _db.Permissions
            .Where(p => list.Contains(p.Code))
            .ToListAsync(ct);
    }

    public Task<bool> UserHasPermissionCodeAsync(
        Guid userId,
        string permissionCode,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .AnyAsync(code => code == permissionCode, ct);

    public async Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var rows = await _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { p.Code, p.Module })
            .Distinct()
            .ToListAsync(ct);

        return rows.Select(r => new PermissionCodeWithModule(r.Code, r.Module)).ToList();
    }

    public async Task<IReadOnlyList<UserPermissionOverrideGrant>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default) =>
        await _db.UserPermissionOverrides
            .Where(o => o.UserId == userId && o.TenantId == tenantId)
            .Join(
                _db.Permissions,
                o => o.PermissionId,
                p => p.Id,
                (o, p) => new UserPermissionOverrideGrant(p.Code, o.GrantType))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserRole>> ListActiveByUserIdAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListUserIdsByRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == roleId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task AddAsync(UserRole userRole, CancellationToken ct = default) =>
        _db.UserRoles.AddAsync(userRole, ct).AsTask();

    public async Task<IReadOnlyList<FeatureAccessGrant>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.FeatureAccessGrants
            .Where(g => g.TenantId == tenantId)
            .ToListAsync(ct);

    public Task AddAsync(AuditLog auditLog, CancellationToken ct = default) =>
        _db.AuditLogs.AddAsync(auditLog, ct).AsTask();

    Task<RoleTemplate?> IRoleTemplateRepository.GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.RoleTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<RoleTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var n = name.Trim();
        return _db.RoleTemplates.FirstOrDefaultAsync(
            t => t.Name.ToLower() == n.ToLower(),
            ct);
    }

    public async Task<IReadOnlyList<RoleTemplate>> ListAsync(CancellationToken ct = default) =>
        await _db.RoleTemplates
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public Task AddAsync(RoleTemplate template, CancellationToken ct = default) =>
        _db.RoleTemplates.AddAsync(template, ct).AsTask();
}
