using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class ManagementCoverageRecordConfiguration : IEntityTypeConfiguration<ManagementCoverageRecord>
{
    public void Configure(EntityTypeBuilder<ManagementCoverageRecord> builder)
    {
        builder.ToTable("management_coverage_records");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.LegalEntityId).IsRequired();
        builder.Property(m => m.OwnerPositionId).IsRequired();
        builder.Property(m => m.CoveredTargetType).HasMaxLength(20).IsRequired();
        builder.Property(m => m.OwnerOrder).IsRequired().HasDefaultValue(1);
        builder.Property(m => m.Source).HasMaxLength(30).IsRequired();
        builder.Property(m => m.IsLocked).IsRequired().HasDefaultValue(true);
        builder.Property(m => m.Status).HasMaxLength(20).IsRequired().HasDefaultValue("active");
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(m => m.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(m => m.OwnerPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(m => m.CoveredPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(m => m.CoveredDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.TenantId).HasDatabaseName("ix_management_coverage_records_tenant_id");
        builder.HasIndex(m => m.LegalEntityId).HasDatabaseName("ix_management_coverage_records_legal_entity_id");
        builder.HasIndex(m => m.OwnerPositionId).HasDatabaseName("ix_management_coverage_records_owner_position_id");
        builder.HasIndex(m => m.CoveredPositionId).HasDatabaseName("ix_management_coverage_records_covered_position_id");
        builder.HasIndex(m => m.CoveredDepartmentId).HasDatabaseName("ix_management_coverage_records_covered_department_id");
        builder.HasIndex(m => new { m.TenantId, m.LegalEntityId, m.OwnerPositionId })
            .HasDatabaseName("ix_management_coverage_records_tenant_legal_entity_owner");

        // Only one active coverage record may exist per covered target per responsibility order -
        // e.g. only one active Primary Manager (owner_order = 1) for a given covered position. The
        // uniqueness is per covered target, not per owner position, so owner_position_id is
        // deliberately excluded. Split into three partial indexes (one per covered target type)
        // because covered_position_id/covered_department_id are nullable and a single composite
        // unique index would not exclude NULL-vs-NULL rows the way a filtered index does.
        builder.HasIndex(m => new { m.TenantId, m.LegalEntityId, m.CoveredPositionId, m.OwnerOrder })
            .IsUnique()
            .HasDatabaseName("ix_management_coverage_records_active_position_order")
            .HasFilter("covered_target_type = 'Position' AND status = 'active'");

        builder.HasIndex(m => new { m.TenantId, m.LegalEntityId, m.CoveredDepartmentId, m.OwnerOrder })
            .IsUnique()
            .HasDatabaseName("ix_management_coverage_records_active_department_order")
            .HasFilter("covered_target_type = 'Department' AND status = 'active'");

        builder.HasIndex(m => new { m.TenantId, m.LegalEntityId, m.OwnerOrder })
            .IsUnique()
            .HasDatabaseName("ix_management_coverage_records_active_company_order")
            .HasFilter("covered_target_type = 'Company' AND status = 'active'");
    }
}
