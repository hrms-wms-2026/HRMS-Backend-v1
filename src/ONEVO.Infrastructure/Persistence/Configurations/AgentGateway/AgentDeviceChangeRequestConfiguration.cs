using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public class AgentDeviceChangeRequestConfiguration : IEntityTypeConfiguration<AgentDeviceChangeRequest>
{
    public void Configure(EntityTypeBuilder<AgentDeviceChangeRequest> builder)
    {
        builder.ToTable("agent_device_change_requests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Status).HasMaxLength(20).IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(500);
        builder.Property(request => request.ReviewComment).HasMaxLength(500);
        builder.Property(request => request.Version).IsRowVersion();

        builder.HasIndex(request => new { request.TenantId, request.EmployeeId, request.Status });
        builder.HasIndex(request => new { request.TenantId, request.EmployeeId })
            .IsUnique()
            .HasFilter("\"status\" = 'pending'");
        builder.HasIndex(request => new { request.TenantId, request.RequestedAgentId });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(request => request.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(request => request.CurrentAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(request => request.RequestedAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
