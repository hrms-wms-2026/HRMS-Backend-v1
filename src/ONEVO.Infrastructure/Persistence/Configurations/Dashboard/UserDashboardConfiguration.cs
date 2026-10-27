using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Dashboard;

public class UserDashboardConfiguration : IEntityTypeConfiguration<UserDashboard>
{
    public void Configure(EntityTypeBuilder<UserDashboard> builder)
    {
        builder.ToTable("user_dashboards");
        builder.HasKey(d => d.Id);
        // Ids are assigned by the repository so a new child added to a tracked graph is Added, not Modified.
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DashboardKey).HasMaxLength(50).IsRequired();
        builder.Property(d => d.LayoutVersion).IsConcurrencyToken();

        // Partial: a soft-deleted row must not block re-creating the dashboard.
        builder.HasIndex(d => new { d.TenantId, d.UserId, d.DashboardKey })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_user_dashboards_tenant_id_user_id_dashboard_key");

        builder.HasMany(d => d.Widgets)
            .WithOne()
            .HasForeignKey(w => w.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
