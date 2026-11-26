using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.AppUsage;

public class AppUsageSnapshotConfiguration : IEntityTypeConfiguration<AppUsageSnapshot>
{
    public void Configure(EntityTypeBuilder<AppUsageSnapshot> builder)
    {
        builder.ToTable("app_usage_snapshots");

        // Task 35: clustered on (tenant_id, captured_at, id) instead of the random GUID id, so new
        // captures append at the end of the table and a date range reads neighbouring pages.
        // The primary key on id stays, nonclustered.
        builder.HasKey(e => e.Id).IsClustered(false);
        builder.HasIndex(e => new { e.TenantId, e.CapturedAt, e.Id })
            .IsClustered()
            .HasDatabaseName("cx_app_usage_snapshots_tenant_captured");

        builder.Property(e => e.ProcessName).HasMaxLength(100);
        builder.Property(e => e.WindowTitleHash).HasMaxLength(128);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_app_usage_snapshots_tenant_employee_captured");

        // The idempotency key: the ingest handler skips captures this device already sent, and since
        // task 35 the database also refuses a second row, so two retries racing cannot both insert.
        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .IsUnique()
            .HasDatabaseName("ix_app_usage_snapshots_tenant_device_captured");
    }
}
