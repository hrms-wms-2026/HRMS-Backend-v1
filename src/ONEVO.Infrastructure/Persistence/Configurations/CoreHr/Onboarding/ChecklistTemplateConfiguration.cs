using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Onboarding;

public sealed class ChecklistTemplateConfiguration : IEntityTypeConfiguration<ChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplate> builder)
    {
        builder.ToTable("checklist_templates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TemplateType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TasksJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.TenantId, x.TemplateType, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.DepartmentId });
        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.TemplateType, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.PositionId });
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Department>().WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>().WithMany()
            .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.Position>().WithMany()
            .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
    }
}
