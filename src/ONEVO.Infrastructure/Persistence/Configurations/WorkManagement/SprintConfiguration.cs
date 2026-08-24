using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("sprints");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.ObjectiveId, s.Status })
            .HasDatabaseName("ix_sprints_tenant_id_objective_id_status");
    }
}
