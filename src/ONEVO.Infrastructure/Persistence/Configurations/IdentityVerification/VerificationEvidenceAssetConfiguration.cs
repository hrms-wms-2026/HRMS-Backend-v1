using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.IdentityVerification;

public sealed class VerificationEvidenceAssetConfiguration
    : IEntityTypeConfiguration<VerificationEvidenceAsset>
{
    public void Configure(EntityTypeBuilder<VerificationEvidenceAsset> builder)
    {
        builder.ToTable(
            "verification_evidence_assets",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_verification_evidence_assets_evidence_type",
                    "evidence_type IN " +
                    "('identity_verification_photo', 'clock_in_photo', 'clock_out_photo', " +
                    "'verification_failure_photo')");
                table.HasCheckConstraint(
                    "ck_verification_evidence_assets_trigger_type",
                    "trigger_type IN ('on_demand', 'clock_in', 'clock_out', 'absence_detected')");
            });
        builder.HasKey(asset => asset.Id);

        builder.Property(asset => asset.EvidenceType).HasMaxLength(40).IsRequired();
        builder.Property(asset => asset.TriggerType).HasMaxLength(20).IsRequired();
        builder.Property(asset => asset.Metadata).HasColumnType("jsonb");

        builder.HasIndex(asset => new
        {
            asset.TenantId,
            asset.EmployeeId,
            asset.CapturedAt
        });
        builder.HasIndex(asset => new { asset.TenantId, asset.VerificationRecordId });
        builder.HasIndex(asset => new { asset.TenantId, asset.PresenceSessionId });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(asset => asset.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationRecord>()
            .WithMany()
            .HasForeignKey(asset => asset.VerificationRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(asset => asset.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(asset => asset.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PresenceSession>()
            .WithMany()
            .HasForeignKey(asset => asset.PresenceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
