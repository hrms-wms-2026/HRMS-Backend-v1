using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.IdentityVerification;

public sealed class VerificationRecordConfiguration : IEntityTypeConfiguration<VerificationRecord>
{
    public void Configure(EntityTypeBuilder<VerificationRecord> builder)
    {
        builder.ToTable(
            "verification_records",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_verification_records_method",
                    "method IN ('photo', 'biometric', 'on_demand_photo')");
                table.HasCheckConstraint(
                    "ck_verification_records_status",
                    "status IN ('pending_review', 'verified', 'failed', 'skipped', 'expired')");
                table.HasCheckConstraint(
                    "ck_verification_records_trigger",
                    "trigger IN ('on_demand', 'clock_in', 'clock_out', 'absence_detected', 'biometric_scan')");
                table.HasCheckConstraint(
                    "ck_verification_records_confidence",
                    "match_confidence IS NULL OR match_confidence BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "ck_verification_records_review_status",
                    "review_status IS NULL OR " +
                    "review_status IN ('pending', 'confirmed_mismatch', 'dismissed_false_positive')");
            });
        builder.HasKey(record => record.Id);

        builder.Property(record => record.Method).HasMaxLength(20).IsRequired();
        builder.Property(record => record.MatchConfidence).HasPrecision(5, 2);
        builder.Property(record => record.Status).HasMaxLength(20).IsRequired();
        builder.Property(record => record.FailureReason).HasMaxLength(255);
        builder.Property(record => record.Trigger).HasMaxLength(20).IsRequired();
        builder.Property(record => record.ReviewStatus).HasMaxLength(30);

        builder.HasIndex(record => new
        {
            record.TenantId,
            record.EmployeeId,
            record.VerifiedAt
        });
        builder.HasIndex(record => new { record.TenantId, record.AgentId, record.CreatedAt });
        builder.HasIndex(record => new { record.TenantId, record.Status, record.CreatedAt });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(record => record.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(record => record.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(record => record.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(record => record.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
