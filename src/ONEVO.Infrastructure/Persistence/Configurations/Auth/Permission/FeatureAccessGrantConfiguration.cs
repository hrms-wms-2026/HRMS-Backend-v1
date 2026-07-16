using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Permission;

public class FeatureAccessGrantConfiguration : IEntityTypeConfiguration<FeatureAccessGrant>
{
    public void Configure(EntityTypeBuilder<FeatureAccessGrant> builder)
    {
        builder.ToTable("feature_access_grants");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.GranteeType).HasMaxLength(10).IsRequired();
        builder.Property(f => f.Module).HasMaxLength(50).IsRequired();

        builder.HasIndex(f => new { f.TenantId, f.GranteeType, f.GranteeId, f.Module }).IsUnique();
    }
}
