using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for platform_service_keys.
/// Phase 1 canonical table - columns match phase1-table-inventory.md exactly.
/// SECURITY: api_key_encrypted is AES-256 encrypted text; never returned by API.
/// </summary>
public class PlatformServiceKeyConfiguration : IEntityTypeConfiguration<PlatformServiceKey>
{
    public void Configure(EntityTypeBuilder<PlatformServiceKey> builder)
    {
        builder.ToTable("platform_service_keys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.ServiceKey)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(k => k.ServiceKey)
            .IsUnique();

        builder.Property(k => k.DisplayName)
            .HasMaxLength(80)
            .IsRequired();

        // Encrypted text column — never returned by API
        builder.Property(k => k.ApiKeyEncrypted)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(k => k.IsActive)
            .IsRequired();

        builder.Property(k => k.LastVerifiedAt);

        builder.Property(k => k.UpdatedById)
            .IsRequired();

        // FK -> platform_users(id), no navigation property needed
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(k => k.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(k => k.UpdatedAt)
            .IsRequired();
    }
}
