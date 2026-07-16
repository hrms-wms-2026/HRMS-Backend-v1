namespace ONEVO.Domain.Features.Auth.Entities;

public class RoleTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModuleKeysJson { get; set; } = "[]";
    public string PermissionCodesJson { get; set; } = "[]";
    public bool IsSystem { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
