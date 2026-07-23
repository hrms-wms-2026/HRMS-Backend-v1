using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentEnrollmentChallengeConfiguration
    : IEntityTypeConfiguration<AgentEnrollmentChallenge>
{
    public void Configure(EntityTypeBuilder<AgentEnrollmentChallenge> builder)
    {
        builder.ToTable("agent_enrollment_challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DeviceId).HasMaxLength(36).IsRequired();
        builder.Property(c => c.DeviceName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.OsVersion).HasMaxLength(50).IsRequired();
        builder.Property(c => c.AgentVersion).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.Property(c => c.AuthorizationCodeHash).HasMaxLength(128);

        builder.HasIndex(c => c.ExpiresAt);
        builder.HasIndex(c => c.DeviceId);
    }
}
