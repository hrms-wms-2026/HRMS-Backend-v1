using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Dashboard;

public class DashboardTemplateConfiguration : IEntityTypeConfiguration<DashboardTemplate>
{
    public void Configure(EntityTypeBuilder<DashboardTemplate> builder)
    {
        builder.ToTable("dashboard_templates", t =>
        {
            // A row is either the tenant's No-role fallback (no role) or a role's template (spec 4.1/4.2).
            t.HasCheckConstraint(
                "ck_dashboard_templates_no_role_flag",
                "(is_no_role_template = 1 AND role_id IS NULL) OR (is_no_role_template = 0 AND role_id IS NOT NULL)");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.DashboardKey).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.IsNoRoleTemplate).HasDefaultValue(false);
        // is_active / version get their SQL defaults (true / 1) in the migration, not here: an EF default on a bool
        // whose CLR default differs would turn an explicit false into the database default.

        // Exactly one No-role template per tenant per dashboard. A partial unique index on the nullable role_id
        // alone would not stop two null-role rows, so the filter is on the flag.
        builder.HasIndex(t => new { t.TenantId, t.DashboardKey })
            .IsUnique()
            .HasFilter("is_no_role_template = 1 AND is_deleted = 0")
            .HasDatabaseName("ux_dashboard_templates_tenant_id_dashboard_key_no_role");

        // One template per role per dashboard.
        builder.HasIndex(t => new { t.TenantId, t.RoleId, t.DashboardKey })
            .IsUnique()
            .HasFilter("role_id IS NOT NULL AND is_deleted = 0")
            .HasDatabaseName("ux_dashboard_templates_tenant_id_role_id_dashboard_key");

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(t => t.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Widgets)
            .WithOne()
            .HasForeignKey(w => w.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
