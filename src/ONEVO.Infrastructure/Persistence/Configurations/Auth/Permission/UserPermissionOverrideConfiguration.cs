using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Permission;

public class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> builder)
    {
        builder.ToTable("user_permission_overrides");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.GrantType).HasMaxLength(10).IsRequired();
        builder.Property(o => o.Reason).HasMaxLength(255);

        builder.HasIndex(o => new { o.TenantId, o.UserId, o.PermissionId }).IsUnique();

        builder.HasOne(o => o.Permission)
               .WithMany(p => p.UserOverrides)
               .HasForeignKey(o => o.PermissionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
