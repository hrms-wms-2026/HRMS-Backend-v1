using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkApprovalRequestConfiguration : IEntityTypeConfiguration<WorkApprovalRequest>
{
    public void Configure(EntityTypeBuilder<WorkApprovalRequest> builder)
    {
        builder.ToTable("wm_approval_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ActionType).HasMaxLength(40).IsRequired();
        builder.Property(r => r.TargetType).HasMaxLength(20).IsRequired();
        builder.Property(r => r.TargetTitle).HasMaxLength(500).IsRequired();
        builder.Property(r => r.ApproverSource).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");
        builder.Property(r => r.DecisionComment).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.ProjectId, r.Status })
            .HasDatabaseName("ix_wm_approval_requests_tenant_id_project_id_status");
        builder.HasIndex(r => new { r.TenantId, r.ApproverEmployeeId, r.Status })
            .HasDatabaseName("ix_wm_approval_requests_tenant_id_approver_employee_id_status");
        builder.HasIndex(r => new { r.TenantId, r.TargetType, r.TargetId, r.ActionType })
            .IsUnique()
            .HasFilter("status = 'pending' AND target_id IS NOT NULL")
            .HasDatabaseName("ux_wm_approval_requests_one_pending_per_target_action");
    }
}
