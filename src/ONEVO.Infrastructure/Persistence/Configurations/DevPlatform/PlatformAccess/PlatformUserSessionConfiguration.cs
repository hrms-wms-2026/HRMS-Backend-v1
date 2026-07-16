using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformUserSessionConfiguration : IEntityTypeConfiguration<PlatformUserSession>
{
    public void Configure(EntityTypeBuilder<PlatformUserSession> builder)
    {
        builder.ToTable("platform_user_sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.IpAddress).HasMaxLength(45);
        builder.Property(s => s.UserAgent).HasMaxLength(500);
        builder.Property(s => s.CsrfTokenHash).HasMaxLength(128);

        builder.HasIndex(s => s.TokenHash).IsUnique();
        builder.HasIndex(s => s.AccountId);
        builder.HasIndex(s => s.ExpiresAt);

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(s => s.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
