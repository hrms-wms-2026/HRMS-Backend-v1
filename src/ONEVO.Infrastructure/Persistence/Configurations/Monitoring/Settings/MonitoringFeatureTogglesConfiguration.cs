using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Settings;

public class MonitoringFeatureTogglesConfiguration : IEntityTypeConfiguration<MonitoringFeatureToggles>
{
    public void Configure(EntityTypeBuilder<MonitoringFeatureToggles> builder)
    {
        builder.ToTable("monitoring_feature_toggles");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.LegalEntityId })
            .IsUnique()
            .HasDatabaseName("ux_monitoring_feature_toggles_tenant_legal_entity");

        builder.HasIndex(e => e.TenantId)
            .IsUnique()
            .HasFilter("legal_entity_id IS NULL")
            .HasDatabaseName("ux_monitoring_feature_toggles_tenant_fallback");

        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
