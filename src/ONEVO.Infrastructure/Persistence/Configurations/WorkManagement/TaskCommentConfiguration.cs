using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Content).HasColumnType("text").IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.TaskId, c.CreatedAt })
            .HasDatabaseName("ix_task_comments_tenant_id_task_id_created_at");
        builder.HasIndex(c => c.ParentCommentId)
            .HasDatabaseName("ix_task_comments_parent_comment_id");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskComment>().WithMany().HasForeignKey(c => c.ParentCommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
