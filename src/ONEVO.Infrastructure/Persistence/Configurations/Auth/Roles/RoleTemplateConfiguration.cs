using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Roles;

public class RoleTemplateConfiguration : IEntityTypeConfiguration<RoleTemplate>
{
    public void Configure(EntityTypeBuilder<RoleTemplate> builder)
    {
        builder.ToTable("role_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();

        builder.Property(t => t.Description).HasMaxLength(255);
        builder.Property(t => t.ModuleKeysJson).HasColumnName("module_keys_json").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.PermissionCodesJson).HasColumnName("permission_codes_json").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.IsSystem).HasColumnName("is_system");
        builder.Property(t => t.Version);
        builder.Property(t => t.IsActive).HasColumnName("is_active");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
    }
}
