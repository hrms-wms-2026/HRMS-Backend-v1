using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Alerts;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class AttendanceAlertDeductionConfiguration : IEntityTypeConfiguration<AttendanceAlertDeduction>
{
    public void Configure(EntityTypeBuilder<AttendanceAlertDeduction> builder)
    {
        builder.ToTable("attendance_alert_deductions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Status).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Multiplier).HasPrecision(6, 2);
        builder.Property(d => d.Hours).HasPrecision(10, 2);

        // One deduction per alert: re-runs update it, never add a second charge.
        builder.HasIndex(d => new { d.TenantId, d.AlertId })
            .IsUnique()
            .HasDatabaseName("ux_attendance_alert_deductions_tenant_alert");
        builder.HasIndex(d => new { d.TenantId, d.EmployeeId, d.Status })
            .HasDatabaseName("ix_attendance_alert_deductions_tenant_employee_status");
    }
}
