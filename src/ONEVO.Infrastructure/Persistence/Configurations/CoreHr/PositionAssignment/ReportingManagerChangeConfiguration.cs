using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.PositionAssignment;

public sealed class ReportingManagerChangeConfiguration : IEntityTypeConfiguration<ReportingManagerChange>
{
    public void Configure(EntityTypeBuilder<ReportingManagerChange> builder)
    {
        builder.ToTable("reporting_manager_changes");
        builder.HasKey(change => change.Id);

        builder.Property(change => change.ChangeReason).HasMaxLength(500);

        builder.HasIndex(change => new { change.TenantId, change.EmployeeId, change.EffectiveAt })
            .HasDatabaseName("ix_reporting_manager_changes_tenant_employee_effective_at");
        builder.HasIndex(change => new { change.TenantId, change.BatchId })
            .HasDatabaseName("ix_reporting_manager_changes_tenant_batch");

        builder.HasOne<EmployeeEntity>()
            .WithMany()
            .HasForeignKey(change => change.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeEntity>()
            .WithMany()
            .HasForeignKey(change => change.PreviousManagerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeEntity>()
            .WithMany()
            .HasForeignKey(change => change.NewManagerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
