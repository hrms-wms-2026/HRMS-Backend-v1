using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfPermissionRepository : IPermissionRepository
{
    private readonly ApplicationDbContext _db;

    public EfPermissionRepository(ApplicationDbContext db)
    {
        _db = db;
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

    public async Task<IReadOnlySet<Guid>> ListUserIdsHoldingPermissionAsync(
        IReadOnlyCollection<Guid> userIds, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return new HashSet<Guid>();

        var matchingUserIds = await _db.UserRoles
            .Where(ur => userIds.Contains(ur.UserId) && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Code })
            .Where(x => x.Code == permissionCode)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        return matchingUserIds.ToHashSet();
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

    public async Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(
        Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
    {
        // UserRole carries TenantId (ITenantOwnedEntity) — filter directly instead of joining Users
        // for tenancy. Join Users to skip soft-deleted and inactive accounts so the empty-approver
        // gate matches who will actually receive notification email.
        var query = _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur, rp })
            .Join(_db.Permissions, x => x.rp.PermissionId, p => p.Id, (x, p) => new { x.ur, p })
            .Where(x => x.p.Code == permissionCode)
            .Join(_db.Users, x => x.ur.UserId, u => u.Id, (x, u) => u)
            .Where(u => !u.IsDeleted && u.IsActive)
            .Select(u => u.Id)
            .Distinct();

        return await query.ToListAsync(ct);
    }
}
