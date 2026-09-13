using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class WorkModeConfiguration : IEntityTypeConfiguration<WorkMode>
{
    public void Configure(EntityTypeBuilder<WorkMode> builder)
    {
        // Temporary table name during Tasks 1-3 to avoid collision with old Lookups.WorkMode.
        // Will be renamed to "work_modes" in Task 4 when old entity is deleted and consumers migrated.
        builder.ToTable("tenant_work_modes");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).HasMaxLength(120).IsRequired();

        builder.HasIndex(w => w.TenantId)
            .HasDatabaseName("ix_tenant_work_modes_tenant_id");

        builder.HasIndex(w => new { w.TenantId, w.LegalEntityId })
            .HasDatabaseName("ix_tenant_work_modes_tenant_id_legal_entity_id");

        builder.HasIndex(w => new { w.TenantId, w.LegalEntityId, w.IsActive })
            .HasDatabaseName("ix_tenant_work_modes_tenant_le_active");

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(w => w.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
