using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.ActivityMonitoring;

public class ActivityRawBufferConfiguration : IEntityTypeConfiguration<ActivityRawBuffer>
{
    public void Configure(EntityTypeBuilder<ActivityRawBuffer> builder)
    {
        builder.ToTable("activity_raw_buffer");
        builder.HasKey(e => e.Id);

        // JSONB for PostgreSQL raw payload storage
        builder.Property(e => e.PayloadJson)
            .IsJsonColumn()
            .IsRequired();

        builder.HasIndex(e => new { e.AgentDeviceId, e.ReceivedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_activity_raw_buffer_device_received");

        builder.HasIndex(e => new { e.TenantId, e.ReceivedAt })
            .HasDatabaseName("ix_activity_raw_buffer_tenant_received");
    }
}
