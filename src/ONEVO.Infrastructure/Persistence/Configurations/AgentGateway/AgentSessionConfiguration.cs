using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentSessionConfiguration : IEntityTypeConfiguration<AgentSession>
{
    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.ToTable("agent_sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DeviceId).HasMaxLength(36).IsRequired();

        // Spec: UNIQUE (device_id) WHERE is_active = true
        builder.HasIndex(s => s.DeviceId)
               .HasFilter("is_active = true")
               .IsUnique();
    }
}
