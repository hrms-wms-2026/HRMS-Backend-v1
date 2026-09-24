using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskPercentageLogConfiguration : IEntityTypeConfiguration<TaskPercentageLog>
{
    public void Configure(EntityTypeBuilder<TaskPercentageLog> builder)
    {
        builder.ToTable("task_percentage_logs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Source).HasMaxLength(20).IsRequired();
        builder.Property(log => log.Reason).HasColumnType("text");

        builder.HasIndex(log => new { log.TenantId, log.TaskId, log.ChangedAt })
            .HasDatabaseName("ix_task_percentage_logs_tenant_id_task_id_changed_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(log => log.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskClockingSession>().WithMany().HasForeignKey(log => log.ClockingSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
