namespace ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

/// <summary>
/// Platform role preset or custom role (canonical `platform_roles`).
/// System roles can be cloned but not deleted. Role names are never authorization
/// rules; authorization checks use permission codes only.
/// </summary>
public class PlatformRole
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
