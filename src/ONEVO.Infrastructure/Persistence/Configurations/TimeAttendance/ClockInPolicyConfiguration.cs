using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class ClockInPolicyConfiguration : IEntityTypeConfiguration<ClockInPolicy>
{
    public void Configure(EntityTypeBuilder<ClockInPolicy> builder)
    {
        builder.ToTable("clock_in_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.ScopeType).HasMaxLength(30).IsRequired();
        builder.Property(p => p.DepartmentIds).HasColumnType("uuid[]");
        builder.Property(p => p.PositionIds).HasColumnType("uuid[]");
        builder.Property(p => p.EmployeeIds).HasColumnType("uuid[]");
        builder.Property(p => p.EitherSourceRule).HasMaxLength(30).IsRequired();
        builder.Property(p => p.FieldPhotoRequirement).HasMaxLength(20).IsRequired();
        builder.Property(p => p.NotificationRecipientResolver).HasMaxLength(50).IsRequired();

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
