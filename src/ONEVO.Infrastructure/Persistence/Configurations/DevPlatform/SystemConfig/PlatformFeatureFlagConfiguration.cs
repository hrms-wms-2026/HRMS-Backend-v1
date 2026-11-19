using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.FeatureFlags.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.SystemConfig;

public class PlatformFeatureFlagConfiguration : IEntityTypeConfiguration<PlatformFeatureFlag>
{
    public void Configure(EntityTypeBuilder<PlatformFeatureFlag> builder)
    {
        builder.ToTable("platform_feature_flags");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Key).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(f => f.Key).IsUnique();
    }
}
