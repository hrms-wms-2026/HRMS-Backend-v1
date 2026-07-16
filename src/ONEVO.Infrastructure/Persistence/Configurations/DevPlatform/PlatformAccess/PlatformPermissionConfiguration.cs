using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformPermissionConfiguration : IEntityTypeConfiguration<PlatformPermission>
{
    public void Configure(EntityTypeBuilder<PlatformPermission> builder)
    {
        builder.ToTable("platform_permissions");
        builder.HasKey(p => p.Code);

        builder.Property(p => p.Code).HasMaxLength(120).ValueGeneratedNever();
        builder.Property(p => p.ModuleKey).HasMaxLength(80).IsRequired();
    }
}
