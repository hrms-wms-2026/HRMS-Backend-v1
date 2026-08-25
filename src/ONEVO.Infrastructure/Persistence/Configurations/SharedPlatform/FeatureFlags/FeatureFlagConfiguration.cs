using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.FeatureFlags;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags");
        builder.HasKey(f => f.Key);
        builder.Property(f => f.Key).HasColumnName("key").HasMaxLength(120);
        builder.Property(f => f.Description).HasColumnName("description");
        builder.Property(f => f.DefaultValue).HasColumnName("default_value").IsRequired();
        builder.Property(f => f.RolloutPercentage).HasColumnName("rollout_percentage").IsRequired();
        builder.Property(f => f.ModuleKey).HasColumnName("module_key").HasMaxLength(100);
        builder.Property(f => f.FeatureKey).HasColumnName("feature_key").HasMaxLength(120);
        builder.Property(f => f.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(f => f.Module)
            .WithMany()
            .HasForeignKey(f => f.ModuleKey)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(f => f.Feature)
            .WithMany()
            .HasForeignKey(f => f.FeatureKey)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(f => f.Overrides)
            .WithOne(o => o.Flag)
            .HasForeignKey(o => o.FlagKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
