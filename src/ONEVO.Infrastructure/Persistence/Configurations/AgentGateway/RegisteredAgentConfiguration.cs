using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class RegisteredAgentConfiguration : IEntityTypeConfiguration<RegisteredAgent>
{
    public void Configure(EntityTypeBuilder<RegisteredAgent> builder)
    {
        builder.ToTable("registered_agents");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.DeviceId).HasMaxLength(36).IsRequired();
        builder.Property(a => a.DeviceName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.OsVersion).HasMaxLength(50).IsRequired();
        builder.Property(a => a.AgentVersion).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();

        // Spec: (tenant_id, device_id) UNIQUE
        builder.HasIndex(a => new { a.TenantId, a.DeviceId }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Status });
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId });
    }
}
