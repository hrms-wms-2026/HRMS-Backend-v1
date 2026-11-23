using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PermissionEntity = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Permission;

public class PermissionConfiguration : IEntityTypeConfiguration<PermissionEntity>
{
    public void Configure(EntityTypeBuilder<PermissionEntity> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(255);
        builder.Property(p => p.Module).HasMaxLength(50);

        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.RetiredAt).HasColumnName("retired_at");

        // A soft-retired code stops working: the resolver, the role screen, entitlement and
        // assignability all read through this filter. Only PermissionCatalogSynchronizer reads past it.
        builder.HasQueryFilter(p => p.RetiredAt == null);

        builder.HasMany(p => p.RolePermissions)
               .WithOne(rp => rp.Permission)
               .HasForeignKey(rp => rp.PermissionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.UserOverrides)
               .WithOne(o => o.Permission)
               .HasForeignKey(o => o.PermissionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
