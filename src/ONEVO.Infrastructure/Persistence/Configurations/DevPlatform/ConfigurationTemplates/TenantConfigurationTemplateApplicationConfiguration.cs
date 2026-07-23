using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.ConfigurationTemplates;

public sealed class TenantConfigurationTemplateApplicationConfiguration
    : IEntityTypeConfiguration<TenantConfigurationTemplateApplication>
{
    public void Configure(EntityTypeBuilder<TenantConfigurationTemplateApplication> builder)
    {
        builder.ToTable(
            "tenant_configuration_template_applications",
            table => table.HasCheckConstraint(
                "ck_tenant_config_template_apps_status",
                "status IN ('applied')"));
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(a => a.ConfigurationTemplateId).HasColumnName("configuration_template_id").IsRequired();
        builder.Property(a => a.TemplateType).HasColumnName("template_type").HasMaxLength(50).IsRequired();
        builder.Property(a => a.AppliedVersion).HasColumnName("applied_version").IsRequired();
        builder.Property(a => a.AppliedPayloadJson).HasColumnName("applied_payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.CustomPayloadJson).HasColumnName("custom_payload_json").HasColumnType("jsonb");
        builder.Property(a => a.WarningsJson).HasColumnName("warnings_json").HasColumnType("jsonb");
        builder.Property(a => a.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(a => a.AppliedById).HasColumnName("applied_by_id").IsRequired();
        builder.Property(a => a.AppliedAt).HasColumnName("applied_at").IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.AppliedAt });
        builder.HasIndex(a => a.ConfigurationTemplateId);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationTemplate>().WithMany().HasForeignKey(a => a.ConfigurationTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(a => a.AppliedById).OnDelete(DeleteBehavior.Restrict);
    }
}
