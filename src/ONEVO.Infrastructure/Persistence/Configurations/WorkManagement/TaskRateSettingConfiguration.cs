using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskRateSettingConfiguration : IEntityTypeConfiguration<TaskRateSetting>
{
    public void Configure(EntityTypeBuilder<TaskRateSetting> builder)
    {
        builder.ToTable("wm_task_rate_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.MaxTaskRate).HasColumnType("numeric(5,4)").IsRequired();

        // One row per Legal Entity.
        builder.HasIndex(s => new { s.TenantId, s.LegalEntityId })
            .HasDatabaseName("ux_wm_task_rate_settings_tenant_id_legal_entity_id")
            .IsUnique();
    }
}
