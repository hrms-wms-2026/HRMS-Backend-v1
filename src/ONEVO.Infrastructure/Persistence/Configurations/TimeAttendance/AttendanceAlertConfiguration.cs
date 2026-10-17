using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Alerts;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public class AttendanceAlertConfiguration : IEntityTypeConfiguration<AttendanceAlert>
{
    public void Configure(EntityTypeBuilder<AttendanceAlert> builder)
    {
        builder.ToTable("attendance_alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.SignalKey).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Severity).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Outcome).HasMaxLength(30);
        builder.Property(a => a.Note).HasMaxLength(2000);
        builder.Property(a => a.DetailsJson).HasColumnType("jsonb");

        // One live alert per employee, day, signal and source; a null source (day-based signal) is one value, not many.
        // Soft-deleted rows (a day that stopped qualifying) do not block the day qualifying again.
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.WorkDate, a.SignalKey, a.SourceId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_attendance_alerts_tenant_employee_date_signal_source");
        builder.HasIndex(a => new { a.TenantId, a.Status, a.EmployeeId })
            .HasDatabaseName("ix_attendance_alerts_tenant_id_status_employee_id");
    }
}
