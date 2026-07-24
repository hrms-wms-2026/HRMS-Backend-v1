using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class ApplicationUsageConfiguration : IEntityTypeConfiguration<ApplicationUsage>
{
    public void Configure(EntityTypeBuilder<ApplicationUsage> builder)
    {
        builder.ToTable("application_usage");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.ProcessName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.ApplicationName).HasMaxLength(255).IsRequired();
        builder.Property(u => u.ApplicationCategory).HasMaxLength(100);
        builder.Property(u => u.WindowTitleHash).HasMaxLength(64);
        builder.HasIndex(u => new { u.TenantId, u.EmployeeId, u.Date });
        builder.HasIndex(u => new { u.TenantId, u.Date, u.ApplicationCategory });
        builder.HasIndex(u => new { u.TenantId, u.EmployeeId, u.Date, u.IsAllowed });
    }
}
