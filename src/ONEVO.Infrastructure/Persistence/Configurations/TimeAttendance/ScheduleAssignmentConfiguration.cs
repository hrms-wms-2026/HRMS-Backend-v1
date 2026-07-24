using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class ScheduleAssignmentConfiguration
    : IEntityTypeConfiguration<ScheduleAssignment>
{
    public void Configure(EntityTypeBuilder<ScheduleAssignment> builder)
    {
        builder.ToTable(
            "schedule_assignments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_schedule_assignments_effective_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint(
                    "ck_schedule_assignments_target",
                    "(assignment_type = 'full_company' AND department_id IS NULL " +
                    "AND position_id IS NULL AND employee_id IS NULL) OR " +
                    "(assignment_type = 'department' AND department_id IS NOT NULL " +
                    "AND position_id IS NULL AND employee_id IS NULL) OR " +
                    "(assignment_type = 'position' AND department_id IS NULL " +
                    "AND position_id IS NOT NULL AND employee_id IS NULL) OR " +
                    "(assignment_type = 'employee' AND department_id IS NULL " +
                    "AND position_id IS NULL AND employee_id IS NOT NULL)");
            });
        builder.HasKey(assignment => assignment.Id);

        builder.Property(assignment => assignment.AssignmentType).HasMaxLength(30).IsRequired();

        builder.HasIndex(assignment => new
        {
            assignment.TenantId,
            assignment.LegalEntityId,
            assignment.AssignmentType,
            assignment.EffectiveFrom
        });
        builder.HasIndex(assignment => new
        {
            assignment.TenantId,
            assignment.EmployeeId,
            assignment.EffectiveFrom
        });
        builder.HasIndex(assignment => new
        {
            assignment.TenantId,
            assignment.DepartmentId,
            assignment.EffectiveFrom
        });
        builder.HasIndex(assignment => new
        {
            assignment.TenantId,
            assignment.PositionId,
            assignment.EffectiveFrom
        });

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkSchedule>()
            .WithMany()
            .HasForeignKey(assignment => assignment.WorkScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(assignment => assignment.PositionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(assignment => assignment.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(assignment => assignment.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
