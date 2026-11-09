using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Dashboard.Entities;

/// <summary>A widget placed by a dashboard template. Its settings are copied to the user's widget.</summary>
public class DashboardTemplateWidget : BaseEntity
{
    public Guid TemplateId { get; set; }
    public string WidgetKey { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public List<DashboardTemplateWidgetLayout> Layouts { get; set; } = new();
}
