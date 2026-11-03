using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> builder)
    {
        builder.ToTable("leave_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);

        builder.HasIndex(p => p.TenantId).HasDatabaseName("ix_leave_policies_tenant_id");
        builder.HasIndex(p => new { p.TenantId, p.LeaveTypeId }).HasDatabaseName("ix_leave_policies_tenant_leave_type");
        builder.HasOne<LeaveType>().WithMany().HasForeignKey(p => p.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeavePolicyVersionConfiguration : IEntityTypeConfiguration<LeavePolicyVersion>
{
    public void Configure(EntityTypeBuilder<LeavePolicyVersion> builder)
    {
        builder.ToTable("leave_policy_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.AmountHours).HasColumnType("numeric(7,2)");
        builder.Property(v => v.AmountPeriod).HasMaxLength(10).IsRequired();
        builder.Property(v => v.CarryForwardCapHours).HasColumnType("numeric(7,2)");
        builder.Property(v => v.EligibleFrom).HasMaxLength(20).IsRequired();
        builder.Property(v => v.MinRequestHours).HasColumnType("numeric(7,2)");
        builder.Property(v => v.MaxRequestHours).HasColumnType("numeric(7,2)");
        builder.Property(v => v.MaxTeamAbsencePercent).HasColumnType("numeric(5,2)");
        builder.Property(v => v.ApprovalMode).HasMaxLength(20).IsRequired();

        builder.HasIndex(v => new { v.LeavePolicyId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ix_leave_policy_versions_policy_version_number");
        builder.HasIndex(v => new { v.LeavePolicyId, v.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("ix_leave_policy_versions_policy_effective_from");
        builder.HasIndex(v => v.TenantId).HasDatabaseName("ix_leave_policy_versions_tenant_id");
        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(v => v.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeavePolicyBlackoutPeriodConfiguration : IEntityTypeConfiguration<LeavePolicyBlackoutPeriod>
{
    public void Configure(EntityTypeBuilder<LeavePolicyBlackoutPeriod> builder)
    {
        builder.ToTable("leave_policy_blackout_periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.LeavePolicyVersionId })
            .HasDatabaseName("ix_leave_policy_blackout_periods_tenant_version");
        builder.HasOne<LeavePolicyVersion>().WithMany().HasForeignKey(x => x.LeavePolicyVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeavePolicyAssignmentConfiguration : IEntityTypeConfiguration<LeavePolicyAssignment>
{
    public void Configure(EntityTypeBuilder<LeavePolicyAssignment> builder)
    {
        builder.ToTable("leave_policy_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TargetKind).HasMaxLength(20).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TargetKind, x.TargetId, x.LeaveTypeId })
            .IsUnique()
            .HasFilter("effective_to IS NULL")
            .HasDatabaseName("ix_leave_policy_assignments_open_target_leave_type");
        builder.HasIndex(x => new { x.TenantId, x.TargetKind, x.TargetId })
            .HasDatabaseName("ix_leave_policy_assignments_tenant_target");
        builder.HasIndex(x => new { x.TenantId, x.LeavePolicyId })
            .HasDatabaseName("ix_leave_policy_assignments_tenant_policy");
        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
