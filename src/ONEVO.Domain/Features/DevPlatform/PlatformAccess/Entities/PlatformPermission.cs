namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Platform-admin permission catalog entry (canonical `platform_permissions`).
/// The code is the primary key (e.g. `platform.tenants.read`). These control
/// Developer Platform modules only; they are not tenant permissions.
/// </summary>
public class PlatformPermission
{
    public string Code { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsHighRisk { get; set; }
}
