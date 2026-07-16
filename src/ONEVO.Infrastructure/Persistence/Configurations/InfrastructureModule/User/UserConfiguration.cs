using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserEntity = ONEVO.Domain.Features.InfrastructureModule.Entities.User;

namespace ONEVO.Infrastructure.Persistence.Configurations.InfrastructureModule.User;

public class UserConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(255).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(128);
        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
    }
}
