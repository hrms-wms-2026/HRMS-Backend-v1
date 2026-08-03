using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Identifier).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Color).HasMaxLength(20);
        builder.Property(p => p.ActualHours).HasColumnType("numeric(18,2)");
        builder.Property(p => p.AllocatedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(p => p.CompletedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(p => p.NextTaskNumber).HasDefaultValue(1L);

        builder.HasIndex(p => new { p.TenantId, p.Identifier })
            .IsUnique()
            .HasDatabaseName("ix_projects_tenant_id_identifier");
        builder.HasIndex(p => new { p.TenantId, p.OwningLegalEntityId, p.UpdatedAt })
            .HasDatabaseName("ix_projects_tenant_id_owning_legal_entity_id_updated_at");
        builder.HasIndex(p => new { p.TenantId, p.CategoryId, p.IsActive })
            .HasDatabaseName("ix_projects_tenant_id_category_id_is_active");

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(p => p.OwningLegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency (xmin) deferred: this Foundation slice only ever
        // inserts Projects, never updates them, so no concurrency token is needed
        // yet. UseXminAsConcurrencyToken() does not exist in the installed Npgsql
        // 10.0.2 package (confirmed by inspecting the assembly) - the update
        // endpoints slice must research the current correct API before adding this.
    }
}
