using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.ModuleCatalog;

public class ModuleCatalogPriceHistoryConfiguration : IEntityTypeConfiguration<ModuleCatalogPriceHistory>
{
    public void Configure(EntityTypeBuilder<ModuleCatalogPriceHistory> builder)
    {
        builder.ToTable("module_catalog_price_history");
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.ModuleKey).HasColumnName("module_key").HasMaxLength(100).IsRequired();
        builder.Property(p => p.OldPricingReference).HasColumnName("old_pricing_reference").HasColumnType("jsonb");
        builder.Property(p => p.NewPricingReference).HasColumnName("new_pricing_reference").HasColumnType("jsonb");
        builder.Property(p => p.OldStorageReference).HasColumnName("old_storage_reference").HasColumnType("jsonb");
        builder.Property(p => p.NewStorageReference).HasColumnName("new_storage_reference").HasColumnType("jsonb");
        builder.Property(p => p.OldAiTokenReference).HasColumnName("old_ai_token_reference").HasColumnType("jsonb");
        builder.Property(p => p.NewAiTokenReference).HasColumnName("new_ai_token_reference").HasColumnType("jsonb");
        builder.Property(p => p.OldPricingUnit).HasColumnName("old_pricing_unit").HasMaxLength(30);
        builder.Property(p => p.NewPricingUnit).HasColumnName("new_pricing_unit").HasMaxLength(30);
        builder.Property(p => p.ChangedById).HasColumnName("changed_by_id").IsRequired();
        builder.Property(p => p.Reason).HasColumnName("reason").IsRequired();
        builder.Property(p => p.ChangedAt).HasColumnName("changed_at").IsRequired();

        builder.HasOne(p => p.Module)
            .WithMany(m => m.PriceHistories)
            .HasForeignKey(p => p.ModuleKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
