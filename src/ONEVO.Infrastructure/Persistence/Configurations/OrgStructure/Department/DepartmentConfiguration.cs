using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".Department" segment would
// collide with the Department entity type and force using-aliases everywhere (same
// convention as LegalEntityConfiguration/PositionConfiguration).
namespace ONEVO.Infrastructure.Persistence.Configurations.OrgStructure;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Code).HasMaxLength(20);

        // Plain tenant index, matching the existing legal_entities/positions
        // tenant-owned convention (RLS enforces isolation at the DB level;
        // this index supports the EF tenant query filter and RLS predicate).
        builder.HasIndex(d => d.TenantId)
            .HasDatabaseName("ix_departments_tenant_id");

        // List-by-legal-entity support. legal_entity_id is itself tenant-unique
        // (a legal entity belongs to exactly one tenant), so this composite index
        // also serves plain "departments in this legal entity" queries via its
        // leading columns.
        builder.HasIndex(d => new { d.TenantId, d.LegalEntityId })
            .HasDatabaseName("ix_departments_tenant_id_legal_entity_id");

        // Name is unique within a Company (legal entity), not tenant-wide - two
        // Companies in the same tenant may each have an "Engineering" department.
        // Unfiltered (not restricted to IsActive), matching the LegalEntityConfiguration
        // precedent for its own tenant+name unique index: a deactivated department's
        // name stays reserved within that legal entity until explicitly renamed.
        builder.HasIndex(d => new { d.TenantId, d.LegalEntityId, d.Name })
            .IsUnique()
            .HasDatabaseName("ix_departments_tenant_id_legal_entity_id_name");

        // Case-insensitive Code uniqueness (scoped by tenant_id + legal_entity_id, ignoring
        // NULL) is enforced by a raw-SQL expression index on lower(code) - see migration
        // AddDepartmentCodeCaseInsensitiveUniqueIndex. Not modeled here: EF Core / Npgsql has
        // no fluent-API support for expression indexes, and this index intentionally is not
        // part of EF's declarative model.

        // Supports recursive (CTE) tree traversal from a parent.
        builder.HasIndex(d => d.ParentDepartmentId)
            .HasDatabaseName("ix_departments_parent_department_id");

        // Supports lookups for the designated head position of a department.
        builder.HasIndex(d => d.HeadPositionId)
            .HasDatabaseName("ix_departments_head_position_id");

        // Company boundary. Restrict (never cascade) so deleting/deactivating a
        // legal entity that still has departments fails loudly instead of
        // silently taking the departments down with it - matches the
        // LegalEntity.ParentLegalEntityId -> LegalEntity FK convention.
        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(d => d.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing parent department. Nullable, and Restrict for the same
        // reason as the legal-entity FK above. Cycle prevention is not DB-enforced
        // here - Part 2B validates it in the application layer via an ancestor walk.
        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(d => d.ParentDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional head position reference. Nullable FK to positions.id.
        // Restrict (never cascade) so deleting a position that is designated as a
        // department head fails loudly instead of silently taking the department down.
        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(d => d.HeadPositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
