using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Settings;

public class MonitoringPolicyOverrideConfiguration : IEntityTypeConfiguration<MonitoringPolicyOverride>
{
    public void Configure(EntityTypeBuilder<MonitoringPolicyOverride> builder)
    {
        builder.ToTable("monitoring_policy_overrides");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ScopeType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OverrideReason).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.ScopeType, e.ScopeId })
            .IsUnique()
            .HasDatabaseName("ux_monitoring_policy_overrides_tenant_scope");
    }
}
