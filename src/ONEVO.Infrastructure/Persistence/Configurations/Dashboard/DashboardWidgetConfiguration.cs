using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Dashboard;

public class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        builder.ToTable("dashboard_widgets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.WidgetKey).HasMaxLength(100).IsRequired();
        builder.Property(w => w.SettingsJson)
            .HasColumnName("settings")
            .HasColumnType("jsonb")
            .IsRequired();

        // Partial: removing a widget soft-deletes the row, and re-adding the same widget must be allowed.
        builder.HasIndex(w => new { w.DashboardId, w.WidgetKey })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_dashboard_widgets_dashboard_id_widget_key");

        builder.HasMany(w => w.Layouts)
            .WithOne()
            .HasForeignKey(l => l.WidgetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
