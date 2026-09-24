using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiOrgStructureSeeder
{
    private static async Task<Dictionary<string, Guid>> SeedRolesAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var definitions = new (string Name, string Description, IReadOnlyList<string> PermissionCodes)[]
        {
            (DapiOrgStructureData.RoleHrManager,
                "Core HR operations for the dapi tenant: org structure, employees, leave.",
                DapiOrgStructureData.HrManagerPermissionCodes),
            (DapiOrgStructureData.RoleGeneralManager,
                "Oversees people and work management across the dapi tenant.",
                DapiOrgStructureData.GeneralManagerPermissionCodes),
            (DapiOrgStructureData.RoleManager,
                "Team-lead role: manages direct reports and their work management access.",
                DapiOrgStructureData.ManagerPermissionCodes),
            (DapiOrgStructureData.RoleEmployee,
                "Baseline organizational role. Self-service access comes from automatic " +
                "module grants (ModuleAutoGrants), not from explicit permissions here.",
                DapiOrgStructureData.EmployeePermissionCodes),
        };

        var roleIdByName = new Dictionary<string, Guid>();

        foreach (var (name, description, permissionCodes) in definitions)
        {
            var roleId = DeterministicGuid($"dapi-org:role:{name}");
            roleIdByName[name] = roleId;

            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
            if (role is null)
            {
                role = new Role
                {
                    Id = roleId,
                    TenantId = DapiTenantId,
                    Name = name,
                    Description = description,
                    IsSystem = true,
                    CreatedAt = now,
                    CreatedById = DapiOwnerUserId
                };
                db.Roles.Add(role);
            }
            else
            {
                role.Name = name;
                role.Description = description;
                role.IsSystem = true;
                role.UpdatedAt = now;
            }

            var permissions = await ResolveExplicitPermissionsAsync(db, permissionCodes, ct);
            foreach (var permission in permissions)
            {
                var exists = await db.RolePermissions.AnyAsync(
                    rp => rp.TenantId == DapiTenantId && rp.RoleId == roleId && rp.PermissionId == permission.Id,
                    ct);
                if (exists)
                {
                    continue;
                }

                db.RolePermissions.Add(new RolePermission
                {
                    TenantId = DapiTenantId,
                    RoleId = roleId,
                    PermissionId = permission.Id
                });
            }
        }

        return roleIdByName;
    }

    private static async Task<List<Permission>> ResolveExplicitPermissionsAsync(
        ApplicationDbContext db,
        IReadOnlyList<string> codes,
        CancellationToken ct)
    {
        var permissions = new List<Permission>(codes.Count);
        foreach (var code in codes)
        {
            var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);
            if (permission is null)
            {
                throw new InvalidOperationException(
                    $"DapiOrgStructureSeeder requires permission code '{code}' but it does not exist " +
                    "in the Permissions table. Add it to PermissionSeeder before seeding dapi org roles.");
            }

            permissions.Add(permission);
        }

        return permissions;
    }

    private static async Task AssignRoleAsync(
        ApplicationDbContext db,
        Guid userId,
        Guid roleId,
        CancellationToken ct)
    {
        // Checks Local first: a role that equals a new hire's own baseline "Employee" role (or
        // any other same-batch duplicate call) would otherwise be Added twice before either add
        // is flushed - AnyAsync alone only sees what's already in the database.
        var alreadyTracked = db.UserRoles.Local.Any(
            ur => ur.TenantId == DapiTenantId && ur.UserId == userId && ur.RoleId == roleId);
        if (alreadyTracked)
        {
            return;
        }

        var exists = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == DapiTenantId && ur.UserId == userId && ur.RoleId == roleId, ct);
        if (exists)
        {
            return;
        }

        db.UserRoles.Add(new UserRole
        {
            TenantId = DapiTenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = DapiOwnerUserId
        });
    }
}
