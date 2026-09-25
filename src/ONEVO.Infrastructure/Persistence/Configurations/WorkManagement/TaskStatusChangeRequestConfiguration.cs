using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskStatusChangeRequestConfiguration : IEntityTypeConfiguration<TaskStatusChangeRequest>
{
    public void Configure(EntityTypeBuilder<TaskStatusChangeRequest> builder)
    {
        builder.ToTable("task_status_change_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.ChangesJson).HasColumnType("jsonb");
        builder.Property(r => r.Note).HasColumnType("text");
        builder.Property(r => r.DecisionComment).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.ProjectId, r.Status })
            .HasDatabaseName("ix_task_status_change_requests_tenant_id_project_id_status");

        builder.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
