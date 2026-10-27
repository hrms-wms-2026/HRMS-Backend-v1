using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement.Planner;

public class PlannedTaskBlockConfiguration : IEntityTypeConfiguration<PlannedTaskBlock>
{
    public void Configure(EntityTypeBuilder<PlannedTaskBlock> builder)
    {
        builder.ToTable("planned_task_blocks", t =>
            t.HasCheckConstraint("ck_planned_task_blocks_end_after_start", "end_at > start_at"));
        builder.HasKey(b => b.Id);

        builder.HasIndex(b => new { b.TenantId, b.EmployeeId, b.StartAt })
            .HasDatabaseName("ix_planned_task_blocks_tenant_id_employee_id_start_at");

        builder.HasOne<Employee>().WithMany().HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WorkTask>().WithMany().HasForeignKey(b => b.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
