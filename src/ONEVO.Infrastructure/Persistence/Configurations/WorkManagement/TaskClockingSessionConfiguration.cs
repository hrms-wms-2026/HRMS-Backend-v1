using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskClockingSessionConfiguration : IEntityTypeConfiguration<TaskClockingSession>
{
    public void Configure(EntityTypeBuilder<TaskClockingSession> builder)
    {
        builder.ToTable("task_clocking_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Reason).HasColumnType("text");

        builder.HasIndex(session => new { session.TenantId, session.TaskId })
            .HasDatabaseName("ix_task_clocking_sessions_one_open_per_task")
            .IsUnique()
            .HasFilter("clock_out_at IS NULL");

        builder.HasIndex(session => new { session.TenantId, session.TaskId, session.ClockInAt })
            .HasDatabaseName("ix_task_clocking_sessions_tenant_id_task_id_clock_in_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(session => session.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
