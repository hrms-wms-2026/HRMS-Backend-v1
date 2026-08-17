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

    public EfAuthRepository(ApplicationDbContext db)
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

    public async Task<Session?> GetLatestActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync(ct);
        return session;
    }

    async Task<Session?> ISessionRepository.GetByIdAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        return session;
    }

    async Task<Session?> ISessionRepository.GetByKeyHashAsync(string keyHash, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
        return session;
    }

    async Task<Session?> ISessionRepository.GetByKeyHashForTenantResolutionAsync(string keyHash, CancellationToken ct)
    {
        // set_config(..., is_local: true) reverts automatically at transaction end, so this never
        // leaks onto the pooled physical connection for a later, unrelated caller to see.
        // CreateExecutionStrategy() wrapping is required because the runtime connection has
        // EnableRetryOnFailure configured - EF Core forbids a user-initiated BeginTransactionAsync
        // under a retrying execution strategy unless it runs inside ExecuteAsync.
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.session_lookup_key_hash', {keyHash}, true)", ct);
            var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
            await transaction.CommitAsync(ct);
            return session;
        });
    }

    async Task ISessionRepository.RevokeByKeyHashAsync(string keyHash, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.KeyHash == keyHash, ct);
        if (session is not null)
        {
            session.IsRevoked = true;
        }
        // Caller must call IUnitOfWork.SaveChangesAsync
    }

    async Task ISessionRepository.RevokeByIdAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null)
        {
            session.IsRevoked = true;
        }
        // Caller must call IUnitOfWork.SaveChangesAsync
    }

    public Task AddAsync(Session session, CancellationToken ct = default)
    {
        var addTask = _db.Sessions.AddAsync(session, ct).AsTask();
        return addTask;
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

    public async Task<Role?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        return role;
    }

    public async Task<Role?> GetByIdForTenantAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);
        return role;
    }

    public async Task<Role?> GetByNameForTenantAsync(Guid tenantId, string name, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == name, ct);
        return role;
    }

    public async Task<Role?> GetBySourceTemplateForTenantAsync(
        Guid tenantId,
        Guid templateId,
        CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.SourceTemplateId == templateId,
            ct);
        return role;
    }

    public async Task<IReadOnlyList<Role>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var roles = await _db.Roles
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
        return roles;
    }

    public Task AddAsync(Role role, CancellationToken ct = default)
    {
        var addTask = _db.Roles.AddAsync(role, ct).AsTask();
        return addTask;
    }

    public void Remove(Role role)
    {
        _db.Roles.Remove(role);
    }

    public async Task<IReadOnlyList<RolePermission>> ListByRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var rolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);
        return rolePermissions;
    }

    public Task AddRangeAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken ct = default)
    {
        var addRangeTask = _db.RolePermissions.AddRangeAsync(rolePermissions, ct);
        return addRangeTask;
    }

    public void RemoveRange(IEnumerable<RolePermission> rolePermissions)
    {
        _db.RolePermissions.RemoveRange(rolePermissions);
    }

    public async Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var permission = await _db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);
        return permission;
    }

    public async Task<IReadOnlyList<Permission>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return Array.Empty<Permission>();
        }

        var query = _db.Permissions.Where(p => idList.Contains(p.Id));
        var permissions = await query.ToListAsync(ct);
        return permissions;
    }

    public async Task<IReadOnlyList<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default)
    {
        var normalizedCodes = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedCodes.Count == 0)
        {
            return Array.Empty<Permission>();
        }

        var query = _db.Permissions.Where(p => normalizedCodes.Contains(p.Code));
        var permissions = await query.ToListAsync(ct);
        return permissions;
    }

    public async Task<bool> UserHasPermissionCodeAsync(
        Guid userId,
        string permissionCode,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code);

        var hasPermission = await query.AnyAsync(code => code == permissionCode, ct);
        return hasPermission;
    }

    public async Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .Distinct();

        var codes = await query.ToListAsync(ct);
        return codes;
    }

    public async Task<IReadOnlyList<PermissionCodeWithModule>> ListRolePermissionCodesWithModulesAsync(
        Guid userId,
        DateTimeOffset now,
        Guid? activeLegalEntityId,
        CancellationToken ct = default)
    {
        var userRoles = _db.UserRoles
            .Where(ur => ur.UserId == userId && (ur.ExpiresAt == null || ur.ExpiresAt > now));

        if (activeLegalEntityId is Guid legalEntityId)
        {
            var entityPositionIds = _db.Positions
                .Where(p => p.LegalEntityId == legalEntityId)
                .Select(p => p.Id);
            userRoles = userRoles.Where(ur =>
                ur.SourcePositionId == null || entityPositionIds.Contains(ur.SourcePositionId!.Value));
        }

        var query = userRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { p.Code, p.Module })
            .Distinct();

        var rows = await query.ToListAsync(ct);
        var result = rows.Select(r => new PermissionCodeWithModule(r.Code, r.Module)).ToList();
        return result;
    }

    public async Task<IReadOnlyList<UserPermissionOverrideGrant>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default)
    {
        var query = _db.UserPermissionOverrides
            .Where(o => o.UserId == userId && o.TenantId == tenantId)
            .Join(
                _db.Permissions,
                o => o.PermissionId,
                p => p.Id,
                (o, p) => new UserPermissionOverrideGrant(p.Code, o.GrantType));

        var grants = await query.ToListAsync(ct);
        return grants;
    }

    public async Task AddAsync(UserPermissionOverride grant, CancellationToken ct = default)
    {
        await _db.UserPermissionOverrides.AddAsync(grant, ct);
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

    public async Task<IReadOnlyList<FeatureAccessGrant>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var query = _db.FeatureAccessGrants
            .Where(g => g.TenantId == tenantId);

        var grants = await query.ToListAsync(ct);
        return grants;
    }

    public Task AddAsync(AuditLog auditLog, CancellationToken ct = default)
    {
        var addTask = _db.AuditLogs.AddAsync(auditLog, ct).AsTask();
        return addTask;
    }

    async Task<RoleTemplate?> IRoleTemplateRepository.GetByIdAsync(Guid id, CancellationToken ct)
    {
        var template = await _db.RoleTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        return template;
    }

    public async Task<RoleTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var n = name.Trim();
        var template = await _db.RoleTemplates.FirstOrDefaultAsync(
            t => t.Name.ToLower() == n.ToLower(),
            ct);
        return template;
    }

    public async Task<IReadOnlyList<RoleTemplate>> ListAsync(CancellationToken ct = default)
    {
        var query = _db.RoleTemplates
            .OrderBy(t => t.Name);

        var templates = await query.ToListAsync(ct);
        return templates;
    }

    public Task AddAsync(RoleTemplate template, CancellationToken ct = default)
    {
        var addTask = _db.RoleTemplates.AddAsync(template, ct).AsTask();
        return addTask;
    }
}
