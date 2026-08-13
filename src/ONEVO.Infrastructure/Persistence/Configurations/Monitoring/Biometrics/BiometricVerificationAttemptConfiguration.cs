using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Biometrics;

public class BiometricVerificationAttemptConfiguration : IEntityTypeConfiguration<BiometricVerificationAttempt>
{
    public void Configure(EntityTypeBuilder<BiometricVerificationAttempt> builder)
    {
        builder.ToTable("biometric_verification_attempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Purpose).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.AwsSessionId).HasMaxLength(200);
        builder.Property(a => a.AwsRegion).HasMaxLength(20).IsRequired();
        builder.Property(a => a.ChallengeType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.FailureCode).HasMaxLength(100);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.Purpose, a.CreatedAt });
        builder.HasIndex(a => new { a.TenantId, a.AwsSessionId }).IsUnique();
    }
}
