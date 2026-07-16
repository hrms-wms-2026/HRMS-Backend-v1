using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.ModuleCatalog;

public class ModuleFeatureConfiguration : IEntityTypeConfiguration<ModuleFeature>
{
    public void Configure(EntityTypeBuilder<ModuleFeature> builder)
    {
        builder.ToTable("module_features");
        builder.HasKey(f => f.FeatureKey);
        builder.Property(f => f.FeatureKey).HasColumnName("feature_key").HasMaxLength(120);
        builder.Property(f => f.ModuleKey).HasColumnName("module_key").HasMaxLength(100).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(150).IsRequired();
        
        builder.HasOne(f => f.Module)
            .WithMany(m => m.Features)
            .HasForeignKey(f => f.ModuleKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
