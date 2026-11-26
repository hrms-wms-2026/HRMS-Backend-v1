using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;
using ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformUserRoles;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminAssignTenantRolePermissions;

namespace ONEVO.Application.Features.DevPlatform.Audit.StateReaders;

// Before/after state for role and permission changes, the audit rows where the previous value
// matters most. Each returns ids or codes only, sorted so a diff between before and after is stable.

public sealed class PlatformRolePermissionsAuditStateReader : IAuditStateReader<UpdatePlatformRolePermissionsCommand>
{
    private readonly IPlatformAccessReadRepository _access;

    public PlatformRolePermissionsAuditStateReader(IPlatformAccessReadRepository access) => _access = access;

    public async Task<object?> ReadAsync(UpdatePlatformRolePermissionsCommand command, CancellationToken ct)
    {
        var rows = await _access.GetRolePermissionsAsync([command.RoleId], ct);
        return new { Permissions = rows.Select(r => r.PermissionCode).OrderBy(c => c, StringComparer.Ordinal).ToArray() };
    }
}

public sealed class PlatformUserRolesAuditStateReader : IAuditStateReader<UpdatePlatformUserRolesCommand>
{
    private readonly IPlatformAccessReadRepository _access;

    public PlatformUserRolesAuditStateReader(IPlatformAccessReadRepository access) => _access = access;

    public async Task<object?> ReadAsync(UpdatePlatformUserRolesCommand command, CancellationToken ct)
    {
        var rows = await _access.GetUserRolesAsync(command.UserId, ct);
        return new { RoleIds = rows.Select(r => r.RoleId).OrderBy(id => id).ToArray() };
    }
}

public sealed class AdminTenantRolePermissionsAuditStateReader : IAuditStateReader<AdminAssignTenantRolePermissionsCommand>
{
    private readonly IRolePermissionRepository _rolePermissions;

    public AdminTenantRolePermissionsAuditStateReader(IRolePermissionRepository rolePermissions)
        => _rolePermissions = rolePermissions;

    public async Task<object?> ReadAsync(AdminAssignTenantRolePermissionsCommand command, CancellationToken ct)
    {
        var rows = await _rolePermissions.ListByRoleAsync(command.RoleId, ct);
        return new { PermissionIds = rows.Select(r => r.PermissionId).OrderBy(id => id).ToArray() };
    }
}

public sealed class TenantRolePermissionsAuditStateReader : IAuditStateReader<AssignRolePermissionsCommand>
{
    private readonly IRolePermissionRepository _rolePermissions;

    public TenantRolePermissionsAuditStateReader(IRolePermissionRepository rolePermissions)
        => _rolePermissions = rolePermissions;

    public async Task<object?> ReadAsync(AssignRolePermissionsCommand command, CancellationToken ct)
    {
        var rows = await _rolePermissions.ListByRoleAsync(command.RoleId, ct);
        return new { PermissionIds = rows.Select(r => r.PermissionId).OrderBy(id => id).ToArray() };
    }
}

public sealed class TenantRoleAuditStateReader : IAuditStateReader<UpdateRoleCommand>
{
    private readonly IRoleRepository _roles;

    public TenantRoleAuditStateReader(IRoleRepository roles) => _roles = roles;

    public async Task<object?> ReadAsync(UpdateRoleCommand command, CancellationToken ct)
    {
        var role = await _roles.GetRoleByIdAsync(command.RoleId, ct);
        return role is null ? null : new { role.Name, role.Description };
    }
}
