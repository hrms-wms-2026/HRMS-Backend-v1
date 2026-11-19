using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.PermissionConsent;

public class MonitoringPermissionConsentConfiguration : IEntityTypeConfiguration<MonitoringPermissionConsent>
{
    public void Configure(EntityTypeBuilder<MonitoringPermissionConsent> builder)
    {
        builder.ToTable("monitoring_permission_consents");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.AcceptedAt })
            .HasDatabaseName("ix_monitoring_permission_consents_tenant_employee_accepted");
    }
}
