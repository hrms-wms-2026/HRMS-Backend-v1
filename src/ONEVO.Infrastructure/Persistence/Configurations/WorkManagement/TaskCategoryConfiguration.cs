using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCategoryConfiguration : IEntityTypeConfiguration<TaskCategory>
{
    public void Configure(EntityTypeBuilder<TaskCategory> builder)
    {
        builder.ToTable("task_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.DisplayOrder })
            .HasDatabaseName("ix_task_categories_tenant_id_project_id_display_order");

        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.Name })
            .IsUnique()
            .HasDatabaseName("ix_task_categories_one_name_per_project");
    }
}
