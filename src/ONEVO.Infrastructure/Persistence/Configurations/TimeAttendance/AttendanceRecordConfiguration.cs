using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable(
            "attendance_records",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_attendance_records_work_time_type",
                    "work_time_type IS NULL OR work_time_type IN ('fixed', 'flexible')");
                table.HasCheckConstraint(
                    "ck_attendance_records_expected_work_area",
                    "expected_work_area IN ('onsite', 'remote', 'either', 'field')");
                table.HasCheckConstraint(
                    "ck_attendance_records_detected_work_area",
                    "detected_work_area IS NULL OR detected_work_area IN ('onsite', 'remote', 'field')");
                table.HasCheckConstraint(
                    "ck_attendance_records_source",
                    "attendance_source IN ('biometric', 'agent', 'web', 'manual', 'mixed')");
                table.HasCheckConstraint(
                    "ck_attendance_records_status",
                    "status IN ('on_time', 'late', 'short_hours', 'absent', " +
                    "'work_area_mismatch', 'on_time_off', 'holiday', 'off_day')");
                table.HasCheckConstraint(
                    "ck_attendance_records_minutes",
                    "worked_minutes >= 0 AND break_minutes >= 0 " +
                    "AND (late_minutes IS NULL OR late_minutes >= 0) " +
                    "AND (short_minutes IS NULL OR short_minutes >= 0)");
            });
        builder.HasKey(record => record.Id);

        builder.Property(record => record.WorkTimeType).HasMaxLength(20);
        builder.Property(record => record.ExpectedWorkArea).HasMaxLength(10).IsRequired();
        builder.Property(record => record.ScheduleTimezone).HasMaxLength(50).IsRequired();
        builder.Property(record => record.HolidayName).HasMaxLength(100);
        builder.Property(record => record.DetectedWorkArea).HasMaxLength(10);
        builder.Property(record => record.AttendanceSource).HasMaxLength(20).IsRequired();
        builder.Property(record => record.Status).HasMaxLength(30).IsRequired();
        builder.Property(record => record.Version).IsRowVersion();

        builder.HasIndex(record => new { record.TenantId, record.EmployeeId, record.Date })
            .IsUnique();
        builder.HasIndex(record => new { record.TenantId, record.Date, record.Status });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(record => record.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkSchedule>()
            .WithMany()
            .HasForeignKey(record => record.WorkScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
