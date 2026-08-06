using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(40);
        builder.Property(p => p.PositionType).HasMaxLength(20).IsRequired().HasDefaultValue("unique");
        builder.Property(p => p.MaxOccupancy).IsRequired().HasDefaultValue(1);
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.CreatedAt).IsRequired();

        // Transitional: LegalEntityId and DepartmentId are nullable to preserve legacy position rows
        builder.Property(p => p.LegalEntityId).IsRequired(false);
        builder.Property(p => p.DepartmentId).IsRequired(false);

        // Foreign keys with DeleteBehavior.Restrict
        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(p => p.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(p => p.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(p => p.ReportsToPositionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(p => p.TenantId).HasDatabaseName("ix_positions_tenant_id");
        builder.HasIndex(p => p.LegalEntityId).HasDatabaseName("ix_positions_legal_entity_id");
        builder.HasIndex(p => p.DepartmentId).HasDatabaseName("ix_positions_department_id");
        builder.HasIndex(p => p.ReportsToPositionId).HasDatabaseName("ix_positions_reports_to_position_id");
        builder.HasIndex(p => new { p.TenantId, p.LegalEntityId }).HasDatabaseName("ix_positions_tenant_id_legal_entity_id");
        builder.HasIndex(p => new { p.TenantId, p.LegalEntityId, p.DepartmentId }).HasDatabaseName("ix_positions_tenant_id_legal_entity_id_department_id");

        builder.HasIndex(p => new { p.TenantId, p.LegalEntityId, p.Name })
            .IsUnique()
            .HasDatabaseName("ix_positions_tenant_id_legal_entity_id_name")
            .HasFilter("legal_entity_id IS NOT NULL");

        builder.HasIndex(p => new { p.TenantId, p.LegalEntityId, p.Code })
            .IsUnique()
            .HasDatabaseName("ix_positions_tenant_id_legal_entity_id_code")
            .HasFilter("code IS NOT NULL AND legal_entity_id IS NOT NULL");
    }
}
