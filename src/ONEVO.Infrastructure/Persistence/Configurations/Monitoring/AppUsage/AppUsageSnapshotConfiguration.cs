using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.AppUsage;

public class AppUsageSnapshotConfiguration : IEntityTypeConfiguration<AppUsageSnapshot>
{
    public void Configure(EntityTypeBuilder<AppUsageSnapshot> builder)
    {
        builder.ToTable("app_usage_snapshots");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProcessName).HasMaxLength(100);
        builder.Property(e => e.WindowTitleHash).HasMaxLength(128);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_app_usage_snapshots_tenant_employee_captured");

        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .HasDatabaseName("ix_app_usage_snapshots_tenant_device_captured");
    }
}
