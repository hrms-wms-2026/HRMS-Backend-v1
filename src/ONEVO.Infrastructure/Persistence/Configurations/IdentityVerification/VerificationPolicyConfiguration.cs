using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.IdentityVerification;

public sealed class VerificationPolicyConfiguration : IEntityTypeConfiguration<VerificationPolicy>
{
    public void Configure(EntityTypeBuilder<VerificationPolicy> builder)
    {
        builder.ToTable(
            "verification_policies",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_verification_policies_context_scope",
                    "photo_capture_context_scope IN " +
                    "('remote_only', 'onsite_only', 'remote_and_onsite', 'disabled')");
                table.HasCheckConstraint(
                    "ck_verification_policies_match_threshold",
                    "match_threshold BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "ck_verification_policies_enrollment_mode",
                    "reference_enrollment_mode IN ('manual_review', 'trusted_sso_auto_approve')");
            });
        builder.HasKey(policy => policy.Id);

        builder.Property(policy => policy.PhotoCaptureContextScope).HasMaxLength(20).IsRequired();
        builder.Property(policy => policy.MatchThreshold)
            .HasPrecision(5, 2)
            .HasDefaultValue(80m)
            .IsRequired();
        builder.Property(policy => policy.ReferenceEnrollmentMode).HasMaxLength(30).IsRequired();

        builder.HasIndex(policy => policy.TenantId).IsUnique();
    }
}
