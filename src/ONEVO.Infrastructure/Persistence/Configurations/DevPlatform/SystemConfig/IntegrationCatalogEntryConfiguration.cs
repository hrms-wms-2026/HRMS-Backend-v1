using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for integration_catalog.
/// Phase 1 canonical table - columns match phase1-table-inventory.md exactly.
/// onevo_app_provider is a FK to the UNIQUE platform_oauth_apps.provider column (not its id).
/// </summary>
public class IntegrationCatalogEntryConfiguration : IEntityTypeConfiguration<IntegrationCatalogEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationCatalogEntry> builder)
    {
        builder.ToTable("integration_catalog");

        builder.HasKey(e => e.IntegrationKey);
        builder.Property(e => e.IntegrationKey)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Description);

        builder.Property(e => e.ConnectionScope)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.OnevoAppProvider)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.LogoUrl)
            .HasMaxLength(500);

        builder.Property(e => e.IsActive)
            .IsRequired();

        builder.Property(e => e.CreatedById)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // FK -> platform_oauth_apps(provider), the app's UNIQUE alternate key (not its id).
        builder.HasOne<PlatformOAuthApp>()
            .WithMany()
            .HasForeignKey(e => e.OnevoAppProvider)
            .HasPrincipalKey(a => a.Provider)
            .OnDelete(DeleteBehavior.Restrict);

        // FK -> platform_users(id), no navigation property needed
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
