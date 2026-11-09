using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>A template widget's grid rectangle at one breakpoint. Desktop is required; tablet/mobile are optional
/// and derived from desktop when missing.</summary>
public class DashboardTemplateWidgetLayout : BaseEntity
{
    public Guid TemplateWidgetId { get; set; }
    public string Breakpoint { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
