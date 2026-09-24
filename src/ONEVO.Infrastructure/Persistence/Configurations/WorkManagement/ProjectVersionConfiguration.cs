using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectVersionConfiguration : IEntityTypeConfiguration<ProjectVersion>
{
    public void Configure(EntityTypeBuilder<ProjectVersion> builder)
    {
        builder.ToTable("versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(v => new { v.TenantId, v.ProjectId, v.StatusId })
            .HasDatabaseName("ix_versions_tenant_id_project_id_status_id");

        builder.HasOne<Project>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VersionStatus>().WithMany().HasForeignKey(v => v.StatusId).OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency (xmin) deferred - see ProjectConfiguration.cs for why.
    }
}
