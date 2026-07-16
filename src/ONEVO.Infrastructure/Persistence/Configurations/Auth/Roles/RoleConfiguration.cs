using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Roles;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(255);

        builder.Property(r => r.SourceTemplateId).HasColumnName("source_template_id");

        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();

        builder.HasIndex(r => new { r.TenantId, r.SourceTemplateId })
            .IsUnique()
            .HasDatabaseName("ix_roles_tenant_id_source_template_id")
            .HasFilter("source_template_id IS NOT NULL");

        builder.HasOne<RoleTemplate>()
            .WithMany()
            .HasForeignKey(r => r.SourceTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.RolePermissions)
               .WithOne(rp => rp.Role)
               .HasForeignKey(rp => rp.RoleId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.UserRoles)
               .WithOne(ur => ur.Role)
               .HasForeignKey(ur => ur.RoleId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
