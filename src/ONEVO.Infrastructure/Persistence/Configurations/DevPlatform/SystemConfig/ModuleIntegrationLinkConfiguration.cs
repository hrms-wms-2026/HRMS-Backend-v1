using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

/// <summary>
/// EF configuration for module_integration_links.
/// Phase 1 canonical table - columns match phase1-table-inventory.md exactly.
/// Composite PK (module_key, integration_key); no surrogate id column.
/// </summary>
public class ModuleIntegrationLinkConfiguration : IEntityTypeConfiguration<ModuleIntegrationLink>
{
    public void Configure(EntityTypeBuilder<ModuleIntegrationLink> builder)
    {
        builder.ToTable("module_integration_links");

        builder.HasKey(l => new { l.ModuleKey, l.IntegrationKey });

        builder.Property(l => l.ModuleKey)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(l => l.IntegrationKey)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.LinkedById)
            .IsRequired();

        builder.Property(l => l.LinkedAt)
            .IsRequired();

        builder.HasOne<ModuleCatalogItem>()
            .WithMany()
            .HasForeignKey(l => l.ModuleKey)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<IntegrationCatalogEntry>()
            .WithMany()
            .HasForeignKey(l => l.IntegrationKey)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(l => l.LinkedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
