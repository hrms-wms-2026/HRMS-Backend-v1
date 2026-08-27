using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskStatusChangeLogConfiguration : IEntityTypeConfiguration<TaskStatusChangeLog>
{
    public void Configure(EntityTypeBuilder<TaskStatusChangeLog> builder)
    {
        builder.ToTable("task_status_change_logs");
        builder.HasKey(log => log.Id);

        builder.HasIndex(log => new { log.TenantId, log.TaskId, log.ChangedAt })
            .HasDatabaseName("ix_task_status_change_logs_tenant_id_task_id_changed_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(log => log.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskStatusEntity>().WithMany().HasForeignKey(log => log.FromStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskStatusEntity>().WithMany().HasForeignKey(log => log.ToStatusId).OnDelete(DeleteBehavior.Restrict);
    }
}
