using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for platform_oauth_app_credentials.
/// Phase 1 canonical table - columns match phase1-table-inventory.md exactly.
/// SECURITY: client_secret_encrypted / private_key_encrypted are AES-256 encrypted text;
/// never returned by API. Business rule: one active row per platform_oauth_app_id.
/// </summary>
public class PlatformOAuthAppCredentialConfiguration : IEntityTypeConfiguration<PlatformOAuthAppCredential>
{
    public void Configure(EntityTypeBuilder<PlatformOAuthAppCredential> builder)
    {
        builder.ToTable("platform_oauth_app_credentials");

        builder.HasKey(c => c.Id);

        // Explicit name: convention would snake-case to platform_o_auth_app_id,
        // inventory requires platform_oauth_app_id.
        builder.Property(c => c.PlatformOAuthAppId)
            .HasColumnName("platform_oauth_app_id")
            .IsRequired();

        builder.HasOne<PlatformOAuthApp>()
            .WithMany()
            .HasForeignKey(c => c.PlatformOAuthAppId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.PlatformOAuthAppId);

        // Encrypted text columns — never returned by API
        builder.Property(c => c.ClientSecretEncrypted)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.PrivateKeyEncrypted)
            .HasColumnType("text");

        builder.Property(c => c.EncryptionKeyVersion)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.CredentialVersion)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.RotatedById)
            .IsRequired();

        // FK -> platform_users(id), no navigation property needed
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(c => c.RotatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.RotatedAt)
            .IsRequired();

        builder.Property(c => c.DeactivatedById);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(c => c.DeactivatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.DeactivatedAt);
    }
}
