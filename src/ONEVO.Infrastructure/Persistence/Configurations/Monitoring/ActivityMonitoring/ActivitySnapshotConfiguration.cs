using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.ActivityMonitoring;

public class ActivitySnapshotConfiguration : IEntityTypeConfiguration<ActivitySnapshot>
{
    public void Configure(EntityTypeBuilder<ActivitySnapshot> builder)
    {
        builder.ToTable("activity_snapshots");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.IntensityScore).HasPrecision(5, 2);
        builder.Property(e => e.ForegroundProcessName).HasMaxLength(100);

        // Range queries by employee capture time
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_activity_snapshots_tenant_employee_captured");

        // Device-scoped audit
        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .HasDatabaseName("ix_activity_snapshots_tenant_device_captured");
    }
}
