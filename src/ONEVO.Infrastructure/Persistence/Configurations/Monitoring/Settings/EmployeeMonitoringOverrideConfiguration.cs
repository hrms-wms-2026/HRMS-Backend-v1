using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Settings;

public class EmployeeMonitoringOverrideConfiguration : IEntityTypeConfiguration<EmployeeMonitoringOverride>
{
    public void Configure(EntityTypeBuilder<EmployeeMonitoringOverride> builder)
    {
        builder.ToTable("employee_monitoring_overrides");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OverrideReason).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_employee_monitoring_overrides_tenant_employee");
    }
}
