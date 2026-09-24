using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Screenshots;

public class AgentCommandConfiguration : IEntityTypeConfiguration<AgentCommand>
{
    public void Configure(EntityTypeBuilder<AgentCommand> builder)
    {
        builder.ToTable("agent_commands");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CommandType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(50).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(e => e.ResultJson).HasColumnType("jsonb");

        // Agent polling: fetch pending non-expired commands for a device
        builder.HasIndex(e => new { e.AgentDeviceId, e.Status, e.ExpiresAt })
            .HasDatabaseName("ix_agent_commands_device_status_expires");

        // HR-side listing by tenant + device + time
        builder.HasIndex(e => new { e.TenantId, e.AgentDeviceId, e.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_agent_commands_tenant_device_created");

        builder.HasOne<TrayDeviceRegistration>()
            .WithMany()
            .HasForeignKey(e => e.AgentDeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
