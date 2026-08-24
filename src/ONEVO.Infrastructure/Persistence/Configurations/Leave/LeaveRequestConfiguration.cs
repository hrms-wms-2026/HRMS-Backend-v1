using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.HalfDayPeriod).HasMaxLength(2);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.TotalDays).HasColumnType("numeric(5,1)");
        builder.Property(r => r.PaidDays).HasColumnType("numeric(5,1)");
        builder.Property(r => r.UnpaidDays).HasColumnType("numeric(5,1)");
        builder.Property(r => r.ConflictSnapshotJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.TenantId, r.EmployeeId }).HasDatabaseName("ix_leave_requests_tenant_employee");
        builder.HasIndex(r => new { r.TenantId, r.Status }).HasDatabaseName("ix_leave_requests_tenant_status");
        builder.HasIndex(r => new { r.TenantId, r.StartDate, r.EndDate })
            .HasDatabaseName("ix_leave_requests_tenant_start_end");

        builder.HasOne<LeaveType>().WithMany().HasForeignKey(r => r.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeaveRequestApproverConfiguration : IEntityTypeConfiguration<LeaveRequestApprover>
{
    public void Configure(EntityTypeBuilder<LeaveRequestApprover> builder)
    {
        builder.ToTable("leave_request_approvers");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Comment).HasMaxLength(500);

        builder.HasIndex(a => new { a.TenantId, a.LeaveRequestId })
            .HasDatabaseName("ix_leave_request_approvers_tenant_request");
        builder.HasIndex(a => new { a.TenantId, a.ApproverEmployeeId, a.Status })
            .HasDatabaseName("ix_leave_request_approvers_tenant_approver_status");

        builder.HasOne<LeaveRequest>().WithMany().HasForeignKey(a => a.LeaveRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.ApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeaveRequestDocumentConfiguration : IEntityTypeConfiguration<LeaveRequestDocument>
{
    public void Configure(EntityTypeBuilder<LeaveRequestDocument> builder)
    {
        builder.ToTable("leave_request_documents");
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => new { d.TenantId, d.LeaveRequestId })
            .HasDatabaseName("ix_leave_request_documents_tenant_request");
        builder.HasOne<LeaveRequest>().WithMany().HasForeignKey(d => d.LeaveRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileRecord>().WithMany().HasForeignKey(d => d.FileRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeaveApprovalDelegateConfiguration : IEntityTypeConfiguration<LeaveApprovalDelegate>
{
    public void Configure(EntityTypeBuilder<LeaveApprovalDelegate> builder)
    {
        builder.ToTable("leave_approval_delegates");
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => new { d.TenantId, d.ApproverEmployeeId })
            .HasDatabaseName("ix_leave_approval_delegates_tenant_approver");
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.ApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.DelegateEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
