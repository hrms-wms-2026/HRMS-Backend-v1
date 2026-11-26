using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class ClockInPolicyConfiguration : IEntityTypeConfiguration<ClockInPolicy>
{
    public void Configure(EntityTypeBuilder<ClockInPolicy> builder)
    {
        builder.HasRowVersionToken(); // task 42: lost-update protection (409 on a stale save)
        builder.ToTable("clock_in_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.ScopeType).HasMaxLength(30).IsRequired();
        builder.Property(p => p.DepartmentIds);
        builder.Property(p => p.PositionIds);
        builder.Property(p => p.EmployeeIds);
        builder.Property(p => p.NotificationRecipientResolver).HasMaxLength(50).IsRequired();
        builder.Property(p => p.DeductionRulesChangedAt).HasColumnName("deduction_rules_changed_at");

        builder.HasIndex(p => p.TenantId)
            .HasDatabaseName("ix_clock_in_policies_tenant_id");

        builder.HasIndex(p => new { p.TenantId, p.LegalEntityId })
            .HasDatabaseName("ix_clock_in_policies_tenant_id_legal_entity_id");

        builder.HasIndex(p => new { p.TenantId, p.LegalEntityId, p.IsActive, p.ScopeType })
            .HasDatabaseName("ix_clock_in_policies_tenant_le_active_scope");

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(p => p.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.LateDeductionRules)
            .WithOne(r => r.ClockInPolicy!)
            .HasForeignKey(r => r.ClockInPolicyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
