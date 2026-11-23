using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.HasRowVersionToken(); // task 42: lost-update protection (409 on a stale save)
        builder.ToTable("sprints");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();

        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.Status })
            .HasDatabaseName("ix_sprints_tenant_id_project_id_status");
    }
}
