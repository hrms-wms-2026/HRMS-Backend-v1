namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Grants a platform permission to a platform role (canonical `platform_role_permissions`).
/// Composite PK (role_id, permission_code). GrantedById is null only for seeded grants,
/// mirroring the nullable-for-seed rule on platform_roles.created_by_id.
/// </summary>
public class PlatformRolePermission
{
    public Guid RoleId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public Guid? GrantedById { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
}
