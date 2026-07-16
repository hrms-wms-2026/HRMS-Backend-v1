using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.ModuleCatalog;

public class ModulePermissionOwnershipConfiguration : IEntityTypeConfiguration<ModulePermissionOwnership>
{
    public void Configure(EntityTypeBuilder<ModulePermissionOwnership> builder)
    {
        builder.ToTable("module_permission_ownership");
        builder.HasKey(p => new { p.ModuleKey, p.PermissionCode });
        builder.Property(p => p.ModuleKey).HasColumnName("module_key").HasMaxLength(100).IsRequired();
        builder.Property(p => p.PermissionCode).HasColumnName("permission_code").HasMaxLength(120).IsRequired();

        builder.HasIndex(p => p.PermissionCode).IsUnique();

        builder.HasOne(p => p.Module)
            .WithMany(m => m.PermissionOwnerships)
            .HasForeignKey(p => p.ModuleKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
