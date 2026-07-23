using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentPolicyConfiguration : IEntityTypeConfiguration<AgentPolicy>
{
    public void Configure(EntityTypeBuilder<AgentPolicy> builder)
    {
        builder.ToTable("agent_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PolicyJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(p => p.AgentId).IsUnique();
    }
}
