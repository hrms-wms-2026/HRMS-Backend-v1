using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class WorkModeConfiguration : IEntityTypeConfiguration<WorkMode>
{
    public void Configure(EntityTypeBuilder<WorkMode> builder)
    {
        builder.ToTable("work_modes");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).HasMaxLength(120).IsRequired();

        builder.HasIndex(w => w.TenantId)
            .HasDatabaseName("ix_work_modes_tenant_id");

        builder.HasIndex(w => new { w.TenantId, w.LegalEntityId })
            .HasDatabaseName("ix_work_modes_tenant_id_legal_entity_id");

        builder.HasIndex(w => new { w.TenantId, w.LegalEntityId, w.IsActive })
            .HasDatabaseName("ix_work_modes_tenant_le_active");

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(w => w.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
