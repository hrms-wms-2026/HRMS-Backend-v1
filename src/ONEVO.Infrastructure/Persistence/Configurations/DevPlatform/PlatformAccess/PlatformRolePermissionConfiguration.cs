using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformRolePermissionConfiguration : IEntityTypeConfiguration<PlatformRolePermission>
{
    public void Configure(EntityTypeBuilder<PlatformRolePermission> builder)
    {
        builder.ToTable("platform_role_permissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionCode });

        builder.Property(rp => rp.PermissionCode).HasMaxLength(120);

        builder.HasOne<PlatformRole>()
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PlatformPermission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionCode)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(rp => rp.GrantedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
