using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class PositionAccessTemplateConfiguration : IEntityTypeConfiguration<PositionAccessTemplate>
{
    public void Configure(EntityTypeBuilder<PositionAccessTemplate> builder)
    {
        builder.ToTable("position_access_templates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId)
            .IsRequired();

        builder.Property(t => t.PositionId)
            .IsRequired();

        builder.Property(t => t.RoleId)
            .IsRequired();

        builder.Property(t => t.RequiresApproval)
            .HasDefaultValue(false);

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.HasIndex(t => t.TenantId);
        builder.HasIndex(t => new { t.TenantId, t.PositionId }).IsUnique();

        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Position>()
            .WithMany()
            .HasForeignKey(t => t.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ONEVO.Domain.Features.Auth.Entities.Role>()
            .WithMany()
            .HasForeignKey(t => t.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
