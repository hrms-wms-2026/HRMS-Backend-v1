using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ShortId).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(500).IsRequired();
        builder.Property(t => t.TaskType).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Priority).HasMaxLength(20).IsRequired();
        builder.Property(t => t.EstimatedHours).HasColumnType("numeric(18,2)");
        builder.Property(t => t.CompletedHours).HasColumnType("numeric(18,2)");

        builder.HasIndex(t => new { t.TenantId, t.ObjectiveId, t.StatusId })
            .HasDatabaseName("ix_tasks_tenant_id_objective_id_status_id");
        builder.HasIndex(t => new { t.TenantId, t.ShortId })
            .IsUnique()
            .HasDatabaseName("ix_tasks_one_short_id_per_tenant");

        builder.HasOne<TaskStatusEntity>().WithMany().HasForeignKey(t => t.StatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Sprint>().WithMany().HasForeignKey(t => t.SprintId).OnDelete(DeleteBehavior.Restrict);
    }
}
