using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.CheckIn;

public class FaceVerificationAttemptConfiguration : IEntityTypeConfiguration<FaceVerificationAttempt>
{
    public void Configure(EntityTypeBuilder<FaceVerificationAttempt> builder)
    {
        builder.ToTable("face_verification_attempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Purpose).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Outcome).HasMaxLength(20).IsRequired();
        builder.Property(a => a.FailureReason).HasMaxLength(50);

        // Recent attempts per employee and purpose, newest first.
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.Purpose, a.CreatedAt })
            .HasDatabaseName("ix_face_verification_attempts_tenant_employee_purpose_created");
    }
}
