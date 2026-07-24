using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Configuration.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Configuration;

public class EmployeeWorkLocationSettingsConfiguration
    : IEntityTypeConfiguration<EmployeeWorkLocationSettings>
{
    public void Configure(EntityTypeBuilder<EmployeeWorkLocationSettings> builder)
    {
        builder.ToTable("employee_work_location_settings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.WorkMode).HasMaxLength(30).IsRequired();
        builder.Property(e => e.WorkLocationVerificationEnabled).IsRequired();
        builder.Property(e => e.PhotoChallengeOnMismatch).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId }).IsUnique();
    }
}
