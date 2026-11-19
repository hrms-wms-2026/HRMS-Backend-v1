using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>
/// A tenant-owned default dashboard layout. Either a role's template (<see cref="RoleId"/> set) or the tenant's
/// No-role template (<see cref="IsNoRoleTemplate"/> true, <see cref="RoleId"/> null), the only fallback. Written by
/// the Administrative application and the startup seeder; the runtime only reads it.
/// </summary>
public class DashboardTemplate : BaseEntity
{
    public string DashboardKey { get; set; } = string.Empty;
    public Guid? RoleId { get; set; }
    public bool IsNoRoleTemplate { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public List<DashboardTemplateWidget> Widgets { get; set; } = new();
}
