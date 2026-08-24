using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Biometrics;

public class BiometricEnrollmentAttemptConfiguration : IEntityTypeConfiguration<BiometricEnrollmentAttempt>
{
    public void Configure(EntityTypeBuilder<BiometricEnrollmentAttempt> builder)
    {
        builder.ToTable("biometric_enrollment_attempts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.AwsSessionId).HasMaxLength(200);
        builder.Property(e => e.Region).HasMaxLength(32);
        builder.Property(e => e.ChallengeType).HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CreatedAt })
            .HasDatabaseName("ix_biometric_enrollment_attempts_tenant_employee_created");
    }
}
