using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> builder)
    {
        builder.ToTable("leave_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Country).HasMaxLength(100);
        builder.Property(p => p.JobLevel).HasMaxLength(100);
        builder.Property(p => p.AccrualMethod).HasMaxLength(20).IsRequired();
        builder.Property(p => p.AccrualStart).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProrationMethod).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ApprovalMode).HasMaxLength(20).IsRequired();
        builder.Property(p => p.MinDaysPerRequest).HasColumnType("numeric(5,1)");
        builder.Property(p => p.MaxTeamAbsencePercent).HasColumnType("numeric(5,2)");
        builder.Property(p => p.FirstYearReducedPercent).HasColumnType("numeric(5,2)");

        builder.HasIndex(p => p.TenantId).HasDatabaseName("ix_leave_policies_tenant_id");
    }
}

public class LeavePolicyLeaveTypeConfiguration : IEntityTypeConfiguration<LeavePolicyLeaveType>
{
    public void Configure(EntityTypeBuilder<LeavePolicyLeaveType> builder)
    {
        builder.ToTable("leave_policy_leave_types");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AnnualEntitlementDays).HasColumnType("numeric(5,1)");
        builder.Property(x => x.CarryForwardMaxDays).HasColumnType("numeric(5,1)");

        builder.HasIndex(x => new { x.TenantId, x.LeavePolicyId, x.LeaveTypeId })
            .IsUnique()
            .HasDatabaseName("ix_leave_policy_leave_types_tenant_policy_type");

        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeavePolicyBlackoutPeriodConfiguration : IEntityTypeConfiguration<LeavePolicyBlackoutPeriod>
{
    public void Configure(EntityTypeBuilder<LeavePolicyBlackoutPeriod> builder)
    {
        builder.ToTable("leave_policy_blackout_periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.LeavePolicyId })
            .HasDatabaseName("ix_leave_policy_blackout_periods_tenant_policy");
        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeavePolicyLegalEntityConfiguration : IEntityTypeConfiguration<LeavePolicyLegalEntity>
{
    public void Configure(EntityTypeBuilder<LeavePolicyLegalEntity> builder)
    {
        builder.ToTable("leave_policy_legal_entities");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId })
            .IsUnique()
            .HasFilter("is_active = true")
            .HasDatabaseName("ix_leave_policy_legal_entities_tenant_legal_entity_active");

        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
