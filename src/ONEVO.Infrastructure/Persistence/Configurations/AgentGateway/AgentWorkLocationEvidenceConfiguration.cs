using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.AgentGateway;

public sealed class AgentWorkLocationEvidenceConfiguration
    : IEntityTypeConfiguration<AgentWorkLocationEvidence>
{
    public void Configure(EntityTypeBuilder<AgentWorkLocationEvidence> builder)
    {
        builder.ToTable(
            "agent_work_location_evidence",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_agent_work_location_evidence_match_status",
                    "match_status IN ('matched', 'mismatch', 'unknown', 'not_evaluated')");
                table.HasCheckConstraint(
                    "ck_agent_work_location_evidence_confidence",
                    "confidence IN ('high', 'medium', 'low', 'unknown')");
                table.HasCheckConstraint(
                    "ck_agent_work_location_evidence_matched_source",
                    "matched_location_source IS NULL OR " +
                    "matched_location_source IN ('company_office', 'remote_profile', 'none')");
            });
        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.PublicIp).HasColumnType("inet").IsRequired();
        builder.Property(evidence => evidence.LocalIp).HasColumnType("inet");
        builder.Property(evidence => evidence.WifiSsid).HasMaxLength(255);
        builder.Property(evidence => evidence.WifiBssidHash).HasMaxLength(100);
        builder.Property(evidence => evidence.GatewayMacHash).HasMaxLength(100);
        builder.Property(evidence => evidence.CoarseLocationJson).HasColumnType("jsonb");
        builder.Property(evidence => evidence.MatchStatus).HasMaxLength(20).IsRequired();
        builder.Property(evidence => evidence.Confidence).HasMaxLength(20).IsRequired();
        builder.Property(evidence => evidence.MatchedLocationSource).HasMaxLength(30);

        builder.HasIndex(evidence => new
        {
            evidence.TenantId,
            evidence.EmployeeId,
            evidence.CapturedAt
        });
        builder.HasIndex(evidence => new { evidence.TenantId, evidence.PresenceSessionId });
        builder.HasIndex(evidence => new
        {
            evidence.TenantId,
            evidence.MatchStatus,
            evidence.CapturedAt
        });

        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(evidence => evidence.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(evidence => evidence.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
