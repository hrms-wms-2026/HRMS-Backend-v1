using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.TrayActivation;

public class TrayDeviceRegistrationConfiguration : IEntityTypeConfiguration<TrayDeviceRegistration>
{
    public void Configure(EntityTypeBuilder<TrayDeviceRegistration> builder)
    {
        builder.ToTable("tray_device_registrations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.DeviceName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.DeviceOs).HasMaxLength(100).IsRequired();
        builder.Property(t => t.DeviceFingerprint).HasMaxLength(512).IsRequired();

        builder.HasIndex(t => new { t.TenantId, t.UserId, t.IsActive });
        builder.HasIndex(t => new { t.TenantId, t.UserId, t.IsActive, t.LastSeenAt });
        builder.HasIndex(t => t.DeviceFingerprint);
    }
}
