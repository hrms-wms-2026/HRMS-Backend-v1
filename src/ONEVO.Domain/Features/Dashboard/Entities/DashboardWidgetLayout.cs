using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>A widget's grid rectangle at one breakpoint (desktop, tablet or mobile).</summary>
public class DashboardWidgetLayout : BaseEntity
{
    public Guid WidgetId { get; set; }
    public string Breakpoint { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
