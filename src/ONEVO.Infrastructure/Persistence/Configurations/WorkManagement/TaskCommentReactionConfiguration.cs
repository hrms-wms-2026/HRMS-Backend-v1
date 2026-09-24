using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCommentReactionConfiguration : IEntityTypeConfiguration<TaskCommentReaction>
{
    public void Configure(EntityTypeBuilder<TaskCommentReaction> builder)
    {
        builder.ToTable("task_comment_reactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Emoji).HasMaxLength(32).IsRequired();

        // One reaction per employee per comment - reacting again replaces the emoji.
        builder.HasIndex(r => new { r.CommentId, r.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ix_task_comment_reactions_comment_id_employee_id");

        builder.HasOne<TaskComment>().WithMany().HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
