namespace ONEVO.Domain.Features.Auth.Entities;

/// <summary>Global permission definitions — not tenant-scoped.</summary>
public class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserPermissionOverride> UserOverrides { get; set; } = new List<UserPermissionOverride>();
}
