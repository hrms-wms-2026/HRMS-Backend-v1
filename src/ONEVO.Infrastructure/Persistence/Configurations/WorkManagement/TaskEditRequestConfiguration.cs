using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskEditRequestConfiguration : IEntityTypeConfiguration<TaskEditRequest>
{
    public void Configure(EntityTypeBuilder<TaskEditRequest> builder)
    {
        builder.ToTable("task_edit_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");
        builder.Property(r => r.DecisionComment).HasColumnType("text");
        builder.Property(r => r.Reason).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.TaskId, r.Status })
            .HasDatabaseName("ix_task_edit_requests_tenant_id_task_id_status");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
