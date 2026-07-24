using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class ActivityRawBufferConfiguration : IEntityTypeConfiguration<ActivityRawBuffer>
{
    public void Configure(EntityTypeBuilder<ActivityRawBuffer> builder)
    {
        builder.ToTable("activity_raw_buffer");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(b => new { b.TenantId, b.AgentDeviceId, b.ReceivedAt });
    }
}
