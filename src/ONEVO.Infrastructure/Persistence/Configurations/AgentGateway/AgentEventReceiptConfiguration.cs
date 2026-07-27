using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentEventReceiptConfiguration : IEntityTypeConfiguration<AgentEventReceipt>
{
    public void Configure(EntityTypeBuilder<AgentEventReceipt> builder)
    {
        builder.ToTable("agent_event_receipts");
        builder.HasKey(r => r.EventId);
        builder.Property(r => r.AgentDeviceId).IsRequired();
        builder.Property(r => r.ProcessedAt).IsRequired();
        builder.HasIndex(r => new { r.AgentDeviceId, r.ProcessedAt });
    }
}
