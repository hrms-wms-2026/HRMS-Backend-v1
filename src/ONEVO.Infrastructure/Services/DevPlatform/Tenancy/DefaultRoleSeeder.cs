using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.DevPlatform.Tenancy;

public sealed class DefaultRoleSeeder : IDefaultRoleSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly IModuleEntitlementService _entitlements;

    public DefaultRoleSeeder(ApplicationDbContext db, IModuleEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
    }

    public async Task<IReadOnlyList<Role>> SeedDefaultRolesAsync(
        Guid tenantId,
        IReadOnlyList<string> moduleKeys,
        CancellationToken ct = default)
    {
        var subscribedPermissions = await _entitlements.GetEntitledPermissionsAsync(moduleKeys, ct);
        var baselinePermissions = await _entitlements.GetEntitledPermissionsAsync(PlatformBaselineModules.Keys, ct);
        var explicitPermissions = subscribedPermissions
            .Concat(baselinePermissions)
            .DistinctBy(p => p.Id)
            .Where(p => p.Code != "*" && !ModuleAutoGrants.Contains(p.Code))
            .ToList();

        var owner = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Owner",
            Description = "Full access to all subscribed modules",
            IsSystem = true
        };

        owner.RolePermissions = explicitPermissions
            .Select(p => new RolePermission { TenantId = tenantId, RoleId = owner.Id, PermissionId = p.Id })
            .ToList();

        var employee = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Employee",
            Description = "Baseline employee self-service access",
            IsSystem = true,
            RolePermissions = []
        };

        _db.Roles.Add(owner);
        _db.Roles.Add(employee);

        return [owner, employee];
    }

    public async Task<Role> SeedOwnerRoleAsync(
        Guid tenantId,
        IReadOnlyList<string> moduleKeys,
        CancellationToken ct = default)
    {
        var roles = await SeedDefaultRolesAsync(tenantId, moduleKeys, ct);
        return roles.First(r => r.Name == "Owner");
    }
}
