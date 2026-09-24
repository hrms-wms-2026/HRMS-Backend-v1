using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class ClockInLateDeductionRuleConfiguration : IEntityTypeConfiguration<ClockInLateDeductionRule>
{
    public void Configure(EntityTypeBuilder<ClockInLateDeductionRule> builder)
    {
        builder.ToTable("clock_in_late_deduction_rules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Multiplier).HasPrecision(5, 2);

        builder.HasIndex(r => r.TenantId)
            .HasDatabaseName("ix_clock_in_late_deduction_rules_tenant_id");

        builder.HasIndex(r => new { r.TenantId, r.ClockInPolicyId })
            .HasDatabaseName("ix_clock_in_late_deduction_rules_tenant_id_policy_id");

        builder.HasIndex(r => new { r.TenantId, r.ClockInPolicyId, r.LateArrivalMinute })
            .IsUnique()
            .HasDatabaseName("ix_clock_in_late_deduction_rules_tenant_policy_minute");

        // time_off_types is not yet implemented in this backend; inventory FK is deferred.
        // Column is retained as uuid so Part 2 can add the FK when Time Off ships.
    }
}
