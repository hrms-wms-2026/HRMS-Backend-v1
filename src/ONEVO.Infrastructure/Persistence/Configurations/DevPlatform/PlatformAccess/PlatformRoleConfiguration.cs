using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.PlatformAccess;

public class PlatformRoleConfiguration : IEntityTypeConfiguration<PlatformRole>
{
    public void Configure(EntityTypeBuilder<PlatformRole> builder)
    {
        builder.ToTable("platform_roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(r => r.Name).IsUnique();

        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
