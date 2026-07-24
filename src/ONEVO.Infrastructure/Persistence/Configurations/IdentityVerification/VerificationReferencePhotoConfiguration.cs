using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.IdentityVerification;

public class VerificationReferencePhotoConfiguration
    : IEntityTypeConfiguration<VerificationReferencePhoto>
{
    public void Configure(EntityTypeBuilder<VerificationReferencePhoto> builder)
    {
        builder.ToTable("verification_reference_photos");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Source).HasMaxLength(30).IsRequired();
        builder.Property(v => v.Status).HasMaxLength(20).IsRequired();
        builder.Property(v => v.ReviewComment).HasMaxLength(255);
        builder.HasIndex(v => new { v.TenantId, v.EmployeeId })
            .HasFilter("is_active = true")
            .IsUnique();
        builder.HasIndex(v => new { v.TenantId, v.EmployeeId, v.Status });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(v => v.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(v => v.PhotoFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegisteredAgent>()
            .WithMany()
            .HasForeignKey(v => v.CapturedDeviceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(v => v.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GdprConsentRecord>()
            .WithMany()
            .HasForeignKey(v => v.LegalAcceptanceRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
