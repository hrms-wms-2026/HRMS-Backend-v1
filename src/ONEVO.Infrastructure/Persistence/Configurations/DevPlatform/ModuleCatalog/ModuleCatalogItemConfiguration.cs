using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.ModuleCatalog;

public class ModuleCatalogItemConfiguration : IEntityTypeConfiguration<ModuleCatalogItem>
{

    public void Configure(EntityTypeBuilder<ModuleCatalogItem> builder)
    {
        builder.ToTable("module_catalog");
        builder.HasKey(m => m.ModuleKey);
        builder.Property(m => m.ModuleKey).HasColumnName("module_key").HasMaxLength(100);
        builder.Property(m => m.Name).HasMaxLength(150).IsRequired();
        builder.Property(m => m.Pillar).HasMaxLength(50).IsRequired();
        builder.Property(m => m.Phase).HasMaxLength(30).IsRequired();
        builder.Property(m => m.PricingUnit).HasMaxLength(30).IsRequired();
        builder.Property(m => m.PricingReference).HasColumnName("pricing_reference").HasColumnType("jsonb");
        builder.Property(m => m.StorageReference).HasColumnName("storage_reference").HasColumnType("jsonb");
        builder.Property(m => m.AiTokenReference).HasColumnName("ai_token_reference").HasColumnType("jsonb");
        
        builder.HasMany(m => m.Features)
            .WithOne(f => f.Module)
            .HasForeignKey(f => f.ModuleKey)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.PermissionOwnerships)
            .WithOne(p => p.Module)
            .HasForeignKey(p => p.ModuleKey)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.PriceHistories)
            .WithOne(p => p.Module)
            .HasForeignKey(p => p.ModuleKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
