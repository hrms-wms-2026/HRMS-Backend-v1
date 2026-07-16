using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("platform_users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(255).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(255).IsRequired();
        builder.Property(u => u.GoogleSub).HasMaxLength(255);
        builder.Property(u => u.Status).HasMaxLength(20).IsRequired();
        builder.Property(u => u.MfaStatus).HasMaxLength(20).IsRequired();
        builder.Property(u => u.InviteStatus).HasMaxLength(20).IsRequired();

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.Status);
        builder.HasIndex(u => u.InviteStatus);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(u => u.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
