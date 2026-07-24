using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class DeviceTrackingConfiguration : IEntityTypeConfiguration<DeviceTracking>
{
    public void Configure(EntityTypeBuilder<DeviceTracking> builder)
    {
        builder.ToTable("device_tracking");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.LaptopPercentage).HasPrecision(5, 2);
        builder.Property(d => d.DetectionMethod).HasMaxLength(30).IsRequired();
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId, d.Date }).IsUnique();
    }
}
