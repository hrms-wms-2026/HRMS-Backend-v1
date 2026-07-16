using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformUserInviteConfiguration : IEntityTypeConfiguration<PlatformUserInvite>
{
    public void Configure(EntityTypeBuilder<PlatformUserInvite> builder)
    {
        builder.ToTable("platform_user_invites");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email).HasMaxLength(255).IsRequired();
        builder.Property(i => i.FullName).HasMaxLength(255).IsRequired();
        builder.Property(i => i.InviteTokenHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(i => i.Email);
        builder.HasIndex(i => i.InviteTokenHash).IsUnique();

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(i => i.InvitedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
