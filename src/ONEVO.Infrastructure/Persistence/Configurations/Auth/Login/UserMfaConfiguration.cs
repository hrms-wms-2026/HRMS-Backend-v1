using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Login;

public class UserMfaConfiguration : IEntityTypeConfiguration<UserMfa>
{
    public void Configure(EntityTypeBuilder<UserMfa> builder)
    {
        builder.ToTable("user_mfa");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MethodType).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Secret).HasMaxLength(512).IsRequired();

        builder.HasIndex(m => new { m.UserId, m.MethodType, m.IsVerified });
    }
}
