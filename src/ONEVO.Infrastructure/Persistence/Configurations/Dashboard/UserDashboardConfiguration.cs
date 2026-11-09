using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        // Roles already merged into this dashboard (auto-sync). A jsonb array of uuids.
        builder.Property(d => d.AppliedRoleIds)
            .HasColumnName("applied_role_ids")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>(),
                new ValueComparer<List<Guid>>(
                    (a, b) => (a ?? new List<Guid>()).SequenceEqual(b ?? new List<Guid>()),
                    v => v.Aggregate(0, (h, g) => HashCode.Combine(h, g.GetHashCode())),
                    v => v.ToList()))
            .HasDefaultValueSql("'[]'")
            .IsRequired();
        builder.Property(d => d.AppliedNoRole)
            .HasColumnName("applied_no_role")
            .HasDefaultValue(false);

        // Partial: a soft-deleted row must not block re-creating the dashboard.
        builder.HasIndex(d => new { d.TenantId, d.UserId, d.DashboardKey })
            .IsUnique()
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("ux_user_dashboards_tenant_id_user_id_dashboard_key");

        builder.HasMany(d => d.Widgets)
            .WithOne()
            .HasForeignKey(w => w.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
