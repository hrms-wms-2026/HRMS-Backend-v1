using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.CheckIn;

public class MonitoringFaceScanConfiguration : IEntityTypeConfiguration<MonitoringFaceScan>
{
    public void Configure(EntityTypeBuilder<MonitoringFaceScan> builder)
    {
        builder.ToTable("monitoring_face_scans");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.StorageKey).HasMaxLength(1000).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(f => new { f.TenantId, f.CheckInId }).IsUnique();
        builder.HasIndex(f => f.StorageKey).IsUnique();
    }
}
