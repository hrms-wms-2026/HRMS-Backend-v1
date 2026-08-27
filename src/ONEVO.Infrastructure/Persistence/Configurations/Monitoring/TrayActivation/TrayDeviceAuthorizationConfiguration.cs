using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Enums;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.TrayActivation;

public class TrayDeviceAuthorizationConfiguration : IEntityTypeConfiguration<TrayDeviceAuthorization>
{
    public void Configure(EntityTypeBuilder<TrayDeviceAuthorization> builder)
    {
        builder.ToTable("tray_device_authorizations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.DeviceCodeHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.UserCodeHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.DeviceFingerprintHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.DeviceName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.DeviceOs).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ClientVersion).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(t => t.DeviceCodeHash).IsUnique();
        builder.HasIndex(t => new { t.UserCodeHash, t.Status, t.ExpiresAt });
        builder.HasIndex(t => new { t.DeviceFingerprintHash, t.CreatedAt });
        builder.HasIndex(t => new { t.Status, t.ExpiresAt });

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_tray_device_authorizations_approval_identity",
                "status NOT IN ('Approved', 'Consumed') OR (approved_tenant_id IS NOT NULL AND approved_user_id IS NOT NULL AND approved_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_tray_device_authorizations_consumed_at",
                "consumed_at IS NULL OR status = 'Consumed'");
        });
    }
}
