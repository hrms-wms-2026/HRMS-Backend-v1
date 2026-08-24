using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveBalanceAuditConfiguration : IEntityTypeConfiguration<LeaveBalanceAudit>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceAudit> builder)
    {
        builder.ToTable("leave_balance_audits");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ChangeType).HasMaxLength(20).IsRequired();
        builder.Property(a => a.DaysChanged).HasColumnType("numeric(5,1)");
        builder.Property(a => a.BalanceAfter).HasColumnType("numeric(5,1)");
        builder.Property(a => a.Reason).HasMaxLength(500);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.LeaveTypeId })
            .HasDatabaseName("ix_leave_balance_audits_tenant_employee_type");

        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeaveType>().WithMany().HasForeignKey(a => a.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LeaveRequest>().WithMany().HasForeignKey(a => a.RelatedRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
