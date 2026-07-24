using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.IdentityVerification.Entities;

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
        builder.HasIndex(v => new { v.TenantId, v.EmployeeId, v.IsActive });
    }
}
