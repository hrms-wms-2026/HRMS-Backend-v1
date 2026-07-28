using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.ConfigurationTemplates;

public sealed class ConfigurationTemplateConfiguration : IEntityTypeConfiguration<ConfigurationTemplate>
{
    public void Configure(EntityTypeBuilder<ConfigurationTemplate> builder)
    {
        builder.ToTable("configuration_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TemplateKey).HasColumnName("template_key").HasMaxLength(100).IsRequired();
        builder.Property(t => t.TemplateType).HasColumnName("template_type").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(t => t.Version).HasColumnName("version").IsRequired();
        builder.Property(t => t.ModuleKeysJson).HasColumnName("module_keys_json").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.IndustryProfileTag).HasColumnName("industry_profile_tag").HasMaxLength(50);
        builder.Property(t => t.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.IsSystem).HasColumnName("is_system").IsRequired();
        builder.Property(t => t.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(t => t.CreatedById).HasColumnName("created_by_id").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(t => t.TemplateKey).IsUnique();
        builder.HasIndex(t => t.TemplateType);

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}
