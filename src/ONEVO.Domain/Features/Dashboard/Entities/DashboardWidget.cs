using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>A widget instance placed on a dashboard. One instance per widget type per dashboard.</summary>
public class DashboardWidget : BaseEntity
{
    public Guid DashboardId { get; set; }
    public string WidgetKey { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public List<DashboardWidgetLayout> Layouts { get; set; } = new();
}
