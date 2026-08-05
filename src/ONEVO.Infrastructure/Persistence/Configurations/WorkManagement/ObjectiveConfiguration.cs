using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ObjectiveConfiguration : IEntityTypeConfiguration<Objective>
{
    public void Configure(EntityTypeBuilder<Objective> builder)
    {
        builder.ToTable("objectives");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Title).HasMaxLength(255).IsRequired();
        builder.Property(o => o.Progress).HasColumnType("numeric(5,2)").HasDefaultValue(0m);
        builder.Property(o => o.ActualHours).HasColumnType("numeric(18,2)");
        builder.Property(o => o.AllocatedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.CompletedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);

        builder.HasIndex(o => new { o.TenantId, o.ProjectId, o.ParentObjectiveId })
            .HasDatabaseName("ix_objectives_tenant_id_project_id_parent_objective_id");
        builder.HasIndex(o => new { o.TenantId, o.OwnerId, o.IsActive })
            .HasDatabaseName("ix_objectives_tenant_id_owner_id_is_active");
        builder.HasIndex(o => new { o.TenantId, o.ReportingManagerId })
            .HasDatabaseName("ix_objectives_tenant_id_reporting_manager_id");
        builder.HasIndex(o => new { o.TenantId, o.ProjectId })
            .IsUnique()
            .HasFilter("is_default = true")
            .HasDatabaseName("ix_objectives_one_default_per_project");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(o => o.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(o => o.ParentObjectiveId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency (xmin) deferred - see ProjectConfiguration.cs for why.
    }
}
