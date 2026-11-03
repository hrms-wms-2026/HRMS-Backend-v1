using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>One user's customizable dashboard (my-work or my-team). LayoutVersion is the optimistic-concurrency token.</summary>
public class UserDashboard : BaseEntity
{
    public Guid UserId { get; set; }
    public string DashboardKey { get; set; } = string.Empty;
    public long LayoutVersion { get; set; } = 1;
    public List<DashboardWidget> Widgets { get; set; } = new();
}
