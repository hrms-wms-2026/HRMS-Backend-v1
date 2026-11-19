using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>One user's customizable dashboard (my-work or my-team). LayoutVersion is the optimistic-concurrency token.</summary>
public class UserDashboard : BaseEntity
{
    public Guid UserId { get; set; }
    public string DashboardKey { get; set; } = string.Empty;
    public long LayoutVersion { get; set; } = 1;

    /// <summary>Roles whose templates have already been merged into this dashboard (auto-sync, spec 6). A role
    /// counts as applied even when it had no template at the time.</summary>
    public List<Guid> AppliedRoleIds { get; set; } = new();

    /// <summary>True when the tenant's No-role template has been merged into this dashboard.</summary>
    public bool AppliedNoRole { get; set; }
    public List<DashboardWidget> Widgets { get; set; } = new();
}
