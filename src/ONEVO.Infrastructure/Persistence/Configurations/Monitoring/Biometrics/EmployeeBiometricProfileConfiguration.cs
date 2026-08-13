using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Biometrics;

public class EmployeeBiometricProfileConfiguration : IEntityTypeConfiguration<EmployeeBiometricProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeBiometricProfile> builder)
    {
        builder.ToTable("employee_biometric_profiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Region).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ReferenceStorageKey).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.Status).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ConsentVersion).HasMaxLength(20).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.EmployeeId });

        // Exactly one Active profile per (tenant, employee) — Postgres partial unique index.
        builder.HasIndex(p => new { p.TenantId, p.EmployeeId })
               .IsUnique()
               .HasFilter("status = 'active'")
               .HasDatabaseName("ix_employee_biometric_profiles_tenant_employee_active");
    }
}
