using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>EF configuration for tray_app_releases (platform-level, no tenant, no RLS).</summary>
public class TrayAppReleaseConfiguration : IEntityTypeConfiguration<TrayAppRelease>
{
    public void Configure(EntityTypeBuilder<TrayAppRelease> builder)
    {
        builder.ToTable("tray_app_releases");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Version).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Channel).HasMaxLength(16).IsRequired();
        builder.Property(r => r.DownloadUrl).HasMaxLength(2048).IsRequired();
        builder.Property(r => r.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(r => r.FileSizeBytes).IsRequired();
        builder.Property(r => r.Publisher).HasMaxLength(200).IsRequired();
        builder.Property(r => r.MinimumWindowsVersion).HasMaxLength(32).IsRequired();
        builder.Property(r => r.MinSupportedVersion).HasMaxLength(32);
        builder.Property(r => r.ReleaseNotes).HasColumnType("text");
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.Source).HasMaxLength(16).IsRequired();
        builder.Property(r => r.CreatedById);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        builder.HasIndex(r => new { r.Channel, r.Version }).IsUnique();
        builder.HasIndex(r => new { r.Channel, r.IsActive });
    }
}
