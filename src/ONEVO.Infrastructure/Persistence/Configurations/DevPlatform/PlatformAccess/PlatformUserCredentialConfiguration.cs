using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public sealed class PlatformUserCredentialConfiguration
    : IEntityTypeConfiguration<PlatformUserCredential>
{
    public void Configure(EntityTypeBuilder<PlatformUserCredential> builder)
    {
        builder.ToTable("platform_user_credentials", table =>
        {
            table.HasCheckConstraint(
                "ck_platform_user_credentials_credential_type",
                "credential_type IN ('password')");
            table.HasCheckConstraint(
                "ck_platform_user_credentials_password_hash",
                "credential_type <> 'password' OR password_hash IS NOT NULL");
        });

        builder.HasKey(value => value.Id);
        builder.Property(value => value.CredentialType).HasMaxLength(40).IsRequired();
        builder.Property(value => value.PasswordHash).HasMaxLength(255);
        builder.Property(value => value.PasswordAlgorithm).HasMaxLength(80);
        builder.Property(value => value.MustChangePassword).HasDefaultValue(false).IsRequired();
        builder.Property(value => value.FailedLoginCount).HasDefaultValue(0).IsRequired();
        builder.Property(value => value.ResetTokenHash).HasMaxLength(255);
        builder.Property(value => value.CreatedAt).IsRequired();

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(value => value.PlatformUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(value => value.PlatformUserId);
        builder.HasIndex(value => value.ResetTokenHash);
        builder.HasIndex(value => new { value.PlatformUserId, value.CredentialType })
            .IsUnique()
            .HasFilter("revoked_at IS NULL");
    }
}
