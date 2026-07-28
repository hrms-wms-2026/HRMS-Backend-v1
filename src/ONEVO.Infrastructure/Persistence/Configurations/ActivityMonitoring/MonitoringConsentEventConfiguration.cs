using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.ActivityMonitoring;

public class MonitoringConsentEventConfiguration : IEntityTypeConfiguration<MonitoringConsentEvent>
{
    public void Configure(EntityTypeBuilder<MonitoringConsentEvent> builder)
    {
        builder.ToTable("monitoring_consent_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Decision).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.IncidentId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.OccurredAt });
    }
}
