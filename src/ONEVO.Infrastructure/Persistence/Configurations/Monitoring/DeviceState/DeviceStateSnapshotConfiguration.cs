using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.DeviceState;

public class DeviceStateSnapshotConfiguration : IEntityTypeConfiguration<DeviceStateSnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceStateSnapshot> builder)
    {
        builder.ToTable("device_state_snapshots");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_device_state_snapshots_tenant_employee_captured");

        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CapturedAt })
            .HasDatabaseName("ix_device_state_snapshots_tenant_device_captured");
    }
}
