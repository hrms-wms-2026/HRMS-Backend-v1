using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public sealed class AgentCommandConfiguration
    : IEntityTypeConfiguration<AgentCommand>
{
    public void Configure(EntityTypeBuilder<AgentCommand> builder)
    {
        builder.ToTable(
            "agent_commands",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_agent_commands_type",
                    "command_type IN ('screenshot_capture_request')");
                table.HasCheckConstraint(
                    "ck_agent_commands_status",
                    "status IN ('pending', 'accepted', 'denied', 'completed', 'failed', 'expired')");
                table.HasCheckConstraint(
                    "ck_agent_commands_decision",
                    "decision IS NULL OR decision IN ('allow', 'deny')");
                table.HasCheckConstraint(
                    "ck_agent_commands_expiry",
                    "expires_at > created_at");
            });
        builder.HasKey(command => command.Id);
        builder.Property(command => command.CommandType)
            .HasMaxLength(60)
            .IsRequired();
        builder.Property(command => command.PayloadJson)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(command => command.Status)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(command => command.Decision)
            .HasMaxLength(10);
        builder.Property(command => command.ConsentNoticeVersion)
            .HasMaxLength(50);
        builder.Property(command => command.ResultCode)
            .HasMaxLength(80);
        builder.Property(command => command.Version).IsRowVersion();

        builder.HasIndex(command => new
        {
            command.TenantId,
            command.AgentId,
            command.Status,
            command.AvailableAt
        });
        builder.HasIndex(command => new
        {
            command.TenantId,
            command.EmployeeId,
            command.CreatedAt
        });
        builder.HasIndex(command => new
        {
            command.TenantId,
            command.AgentId,
            command.CommandType
        })
            .IsUnique()
            .HasFilter("status IN ('pending', 'accepted')");

        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(command => command.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(command => command.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MonitoringEvidenceAsset>()
            .WithMany()
            .HasForeignKey(command => command.MonitoringEvidenceAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

