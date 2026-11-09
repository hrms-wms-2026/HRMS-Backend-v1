using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Dashboard;

public class DashboardTemplateWidgetLayoutConfiguration : IEntityTypeConfiguration<DashboardTemplateWidgetLayout>
{
    public void Configure(EntityTypeBuilder<DashboardTemplateWidgetLayout> builder)
    {
        builder.ToTable("dashboard_template_widget_layouts", t =>
        {
            t.HasCheckConstraint("ck_dashboard_template_widget_layouts_x", "x >= 0");
            t.HasCheckConstraint("ck_dashboard_template_widget_layouts_y", "y >= 0");
            t.HasCheckConstraint("ck_dashboard_template_widget_layouts_w", "w > 0");
            t.HasCheckConstraint("ck_dashboard_template_widget_layouts_h", "h > 0");
            t.HasCheckConstraint(
                "ck_dashboard_template_widget_layouts_breakpoint",
                $"breakpoint IN ('{Breakpoints.Desktop}', '{Breakpoints.Tablet}', '{Breakpoints.Mobile}')");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Breakpoint).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TemplateWidgetId, l.Breakpoint })
            .IsUnique()
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("ux_dashboard_template_widget_layouts_template_widget_id_breakpoint");
    }
}
