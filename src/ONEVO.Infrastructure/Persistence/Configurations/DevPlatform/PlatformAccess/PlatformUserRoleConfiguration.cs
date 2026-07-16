using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformUserRoleConfiguration : IEntityTypeConfiguration<PlatformUserRole>
{
    public void Configure(EntityTypeBuilder<PlatformUserRole> builder)
    {
        builder.ToTable("platform_user_roles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PlatformRole>()
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(ur => ur.AssignedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
