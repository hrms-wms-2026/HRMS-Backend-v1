using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkApprovalCommentConfiguration : IEntityTypeConfiguration<WorkApprovalComment>
{
    public void Configure(EntityTypeBuilder<WorkApprovalComment> builder)
    {
        builder.ToTable("wm_approval_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SubjectType).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Content).HasMaxLength(4000).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.SubjectType, c.SubjectId, c.CreatedAt })
            .HasDatabaseName("ix_wm_approval_comments_tenant_id_subject_type_subject_id_created_at");
        builder.HasIndex(c => c.ParentCommentId)
            .HasDatabaseName("ix_wm_approval_comments_parent_comment_id");

        builder.HasOne<WorkApprovalComment>().WithMany().HasForeignKey(c => c.ParentCommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
