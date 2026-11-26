using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.ActivityMonitoring;

public class ActivitySnapshotConfiguration : IEntityTypeConfiguration<ActivitySnapshot>
{
    public void Configure(EntityTypeBuilder<ActivitySnapshot> builder)
    {
        builder.ToTable("activity_snapshots");

        // Task 35: clustered on (tenant_id, captured_at, id) instead of the random GUID id, so new
        // captures append at the end of the table and a date range reads neighbouring pages.
        // The primary key on id stays, nonclustered.
        builder.HasKey(e => e.Id).IsClustered(false);
        builder.HasIndex(e => new { e.TenantId, e.CapturedAt, e.Id })
            .IsClustered()
            .HasDatabaseName("cx_activity_snapshots_tenant_captured");

        builder.Property(e => e.IntensityScore).HasPrecision(5, 2);
        builder.Property(e => e.ForegroundProcessName).HasMaxLength(100);

        // Range queries by employee capture time
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_activity_snapshots_tenant_employee_captured");

        // Device-scoped audit, and the idempotency key that stops a tray retry/resend from being
        // ingested twice — GetMyWorkPatternQueryHandler sums ActiveSeconds/IdleSeconds across every
        // row for the day, so a duplicated capture interval silently inflates those totals.
        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .IsUnique()
            .HasDatabaseName("ix_activity_snapshots_tenant_device_captured");
    }
}
