using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentHealthLogConfiguration : IEntityTypeConfiguration<AgentHealthLog>
{
    public void Configure(EntityTypeBuilder<AgentHealthLog> builder)
    {
        builder.ToTable("agent_health_logs");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.ErrorsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(h => h.CpuUsage).HasPrecision(5, 2);

        // Spec: (agent_id, reported_at)
        builder.HasIndex(h => new { h.AgentId, h.ReportedAt });
    }
}
