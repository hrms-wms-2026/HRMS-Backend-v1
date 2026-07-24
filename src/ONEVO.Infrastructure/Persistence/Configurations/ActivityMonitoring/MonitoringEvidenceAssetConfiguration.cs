using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class MonitoringEvidenceAssetConfiguration : IEntityTypeConfiguration<MonitoringEvidenceAsset>
{
    public void Configure(EntityTypeBuilder<MonitoringEvidenceAsset> builder)
    {
        builder.ToTable("monitoring_evidence_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EvidenceType).HasMaxLength(40).IsRequired();
        builder.Property(a => a.TriggerType).HasMaxLength(20).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.CapturedAt });
    }
}
