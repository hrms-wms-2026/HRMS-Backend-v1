using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskStatusConfiguration : IEntityTypeConfiguration<TaskStatusEntity>
{
    public void Configure(EntityTypeBuilder<TaskStatusEntity> builder)
    {
        builder.ToTable("task_statuses");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Visibility).HasMaxLength(20).IsRequired().HasDefaultValue(TaskStatusVisibilities.Public);
        builder.Property(s => s.Category).HasMaxLength(20).IsRequired().HasDefaultValue(TaskStatusCategories.NotStarted);
        builder.Property(s => s.Color).HasMaxLength(7).IsRequired().HasDefaultValue("#94A3B8");

        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId, s.DisplayOrder })
            .HasDatabaseName("ix_task_statuses_tenant_id_project_id_objective_id_display_order");

        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId, s.Name })
            .IsUnique()
            .HasDatabaseName("ix_task_statuses_one_name_per_scope");
    }
}
