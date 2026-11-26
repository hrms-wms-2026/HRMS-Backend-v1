using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.DeviceState;

public class DeviceStateSnapshotConfiguration : IEntityTypeConfiguration<DeviceStateSnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceStateSnapshot> builder)
    {
        builder.ToTable("device_state_snapshots");

        // Task 35: clustered on (tenant_id, captured_at, id) instead of the random GUID id, so new
        // captures append at the end of the table and a date range reads neighbouring pages.
        // The primary key on id stays, nonclustered.
        builder.HasKey(e => e.Id).IsClustered(false);
        builder.HasIndex(e => new { e.TenantId, e.CapturedAt, e.Id })
            .IsClustered()
            .HasDatabaseName("cx_device_state_snapshots_tenant_captured");

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_device_state_snapshots_tenant_employee_captured");

        // The idempotency key: the ingest handler skips captures this device already sent, and since
        // task 35 the database also refuses a second row, so two retries racing cannot both insert.
        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .IsUnique()
            .HasDatabaseName("ix_device_state_snapshots_tenant_device_captured");
    }
}
