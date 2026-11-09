using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Dashboard;

public class DashboardTemplateWidgetConfiguration : IEntityTypeConfiguration<DashboardTemplateWidget>
{
    public void Configure(EntityTypeBuilder<DashboardTemplateWidget> builder)
    {
        builder.ToTable("dashboard_template_widgets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.WidgetKey).HasMaxLength(100).IsRequired();
        builder.Property(w => w.SettingsJson)
            .HasColumnName("settings")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.HasIndex(w => new { w.TemplateId, w.WidgetKey })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_dashboard_template_widgets_template_id_widget_key");

        builder.HasMany(w => w.Layouts)
            .WithOne()
            .HasForeignKey(l => l.TemplateWidgetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
