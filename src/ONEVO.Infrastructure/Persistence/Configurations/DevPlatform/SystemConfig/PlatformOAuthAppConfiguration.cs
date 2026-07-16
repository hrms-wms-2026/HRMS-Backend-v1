using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for platform_oauth_apps.
/// Phase 1 canonical table - columns match phase1-table-inventory.md exactly.
/// client_id is deliberately NOT encrypted (used in redirect URLs);
/// secret material lives in platform_oauth_app_credentials.
/// </summary>
public class PlatformOAuthAppConfiguration : IEntityTypeConfiguration<PlatformOAuthApp>
{
    public void Configure(EntityTypeBuilder<PlatformOAuthApp> builder)
    {
        builder.ToTable("platform_oauth_apps");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Provider)
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(a => a.Provider)
            .IsUnique();

        builder.Property(a => a.AppName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.LogoUrl)
            .HasMaxLength(500);

        builder.Property(a => a.ClientId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.AuthorizationUrl)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.TokenUrl)
            .HasMaxLength(500)
            .IsRequired();

        // Postgres text[] per inventory
        builder.Property(a => a.DefaultScopes)
            .IsRequired();

        builder.Property(a => a.IsActive)
            .IsRequired();

        builder.Property(a => a.LastVerifiedAt);

        builder.Property(a => a.UpdatedById)
            .IsRequired();

        // FK -> platform_users(id), no navigation property needed
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(a => a.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.UpdatedAt)
            .IsRequired();
    }
}
