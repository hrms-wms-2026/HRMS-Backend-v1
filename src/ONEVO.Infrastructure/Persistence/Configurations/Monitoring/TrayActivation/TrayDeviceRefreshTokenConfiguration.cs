using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.TrayActivation;

public class TrayDeviceRefreshTokenConfiguration : IEntityTypeConfiguration<TrayDeviceRefreshToken>
{
    public void Configure(EntityTypeBuilder<TrayDeviceRefreshToken> builder)
    {
        builder.ToTable("tray_device_refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(100);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.DeviceRegistrationId, t.IsRevoked });

        builder.Ignore(t => t.IsValid);
    }
}
