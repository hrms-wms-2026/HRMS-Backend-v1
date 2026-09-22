using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCommentLogConfiguration : IEntityTypeConfiguration<TaskCommentLog>
{
    public void Configure(EntityTypeBuilder<TaskCommentLog> builder)
    {
        builder.ToTable("task_comment_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Action).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.TaskId, l.OccurredAt })
            .HasDatabaseName("ix_task_comment_logs_tenant_id_task_id_occurred_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskComment>().WithMany().HasForeignKey(l => l.CommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
