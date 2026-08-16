using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCreationRequestConfiguration : IEntityTypeConfiguration<TaskCreationRequest>
{
    public void Configure(EntityTypeBuilder<TaskCreationRequest> builder)
    {
        builder.ToTable("task_creation_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");
        builder.Property(r => r.DecisionComment).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.ObjectiveId, r.Status })
            .HasDatabaseName("ix_task_creation_requests_tenant_id_objective_id_status");

        builder.HasOne<Objective>().WithMany().HasForeignKey(r => r.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkTask>().WithMany().HasForeignKey(r => r.CreatedTaskId).OnDelete(DeleteBehavior.SetNull);
    }
}
