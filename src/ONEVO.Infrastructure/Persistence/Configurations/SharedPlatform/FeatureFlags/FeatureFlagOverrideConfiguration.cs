using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.FeatureFlags;

public class FeatureFlagOverrideConfiguration : IEntityTypeConfiguration<FeatureFlagOverride>
{
    public void Configure(EntityTypeBuilder<FeatureFlagOverride> builder)
    {
        builder.ToTable("feature_flag_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.FlagKey).HasColumnName("flag_key").HasMaxLength(120).IsRequired();
        builder.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(o => o.Value).HasColumnName("value").IsRequired();
        builder.Property(o => o.GrantedById).HasColumnName("granted_by_id").IsRequired();
        builder.Property(o => o.GrantedAt).HasColumnName("granted_at").IsRequired();
        builder.Property(o => o.Reason).HasColumnName("reason");

        builder.HasIndex(o => new { o.FlagKey, o.TenantId }).IsUnique();

        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities.PlatformUser>()
            .WithMany()
            .HasForeignKey(o => o.GrantedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
