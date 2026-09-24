using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class SprintActivityLogConfiguration : IEntityTypeConfiguration<SprintActivityLog>
{
    public void Configure(EntityTypeBuilder<SprintActivityLog> builder)
    {
        builder.ToTable("sprint_activity_logs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Action).HasMaxLength(30).IsRequired();
        builder.Property(log => log.FromStatus).HasMaxLength(20);
        builder.Property(log => log.ToStatus).HasMaxLength(20);

        builder.HasIndex(log => new { log.TenantId, log.SprintId, log.OccurredAt })
            .HasDatabaseName("ix_sprint_activity_logs_tenant_id_sprint_id_occurred_at");

        builder.HasOne<Sprint>().WithMany().HasForeignKey(log => log.SprintId).OnDelete(DeleteBehavior.Restrict);
    }
}
