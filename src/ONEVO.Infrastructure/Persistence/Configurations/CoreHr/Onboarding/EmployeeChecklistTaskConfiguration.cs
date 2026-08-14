using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Onboarding;

public sealed class EmployeeChecklistTaskConfiguration : IEntityTypeConfiguration<EmployeeChecklistTask>
{
    public void Configure(EntityTypeBuilder<EmployeeChecklistTask> builder)
    {
        builder.ToTable("employee_checklist_tasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LifecycleType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TaskTitle).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OwnerType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsRequired).HasDefaultValue(true).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LifecycleType, x.Sequence });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChecklistTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>().WithMany().HasForeignKey(x => x.AssignedToId).OnDelete(DeleteBehavior.Restrict);
    }
}
