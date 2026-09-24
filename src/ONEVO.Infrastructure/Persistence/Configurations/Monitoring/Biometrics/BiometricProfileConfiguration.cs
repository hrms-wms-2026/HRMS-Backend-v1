using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Biometrics;

public class BiometricProfileConfiguration : IEntityTypeConfiguration<BiometricProfile>
{
    public void Configure(EntityTypeBuilder<BiometricProfile> builder)
    {
        builder.ToTable("biometric_profiles");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_biometric_profiles_tenant_employee");
    }
}
