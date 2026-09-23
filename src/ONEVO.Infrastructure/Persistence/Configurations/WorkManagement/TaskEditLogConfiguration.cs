using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskEditLogConfiguration : IEntityTypeConfiguration<TaskEditLog>
{
    public void Configure(EntityTypeBuilder<TaskEditLog> builder)
    {
        builder.ToTable("task_edit_logs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Source).HasMaxLength(20).IsRequired();
        builder.Property(log => log.OldValuesJson).HasColumnType("jsonb");
        builder.Property(log => log.NewValuesJson).HasColumnType("jsonb");
        builder.Property(log => log.Reason).HasColumnType("text");

        builder.HasIndex(log => new { log.TenantId, log.TaskId, log.ChangedAt })
            .HasDatabaseName("ix_task_edit_logs_tenant_id_task_id_changed_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(log => log.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskEditRequest>().WithMany().HasForeignKey(log => log.EditRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
