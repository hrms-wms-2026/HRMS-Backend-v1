namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class ModulePermissionOwnership
{
    public string ModuleKey { get; set; } = string.Empty;
    public string PermissionCode { get; set; } = string.Empty;
    public bool IsDefaultPermission { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ModuleCatalogItem Module { get; set; } = null!;
}
