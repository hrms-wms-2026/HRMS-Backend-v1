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

        builder.HasIndex(e => e.TenantId)
            .IsUnique()
            .HasDatabaseName("ux_monitoring_feature_toggles_tenant");
    }
}
