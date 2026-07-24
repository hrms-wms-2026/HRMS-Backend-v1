using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class WorkScheduleDayConfiguration : IEntityTypeConfiguration<WorkScheduleDay>
{
    public void Configure(EntityTypeBuilder<WorkScheduleDay> builder)
    {
        builder.ToTable(
            "work_schedule_days",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_work_schedule_days_day_of_week",
                    "day_of_week BETWEEN 1 AND 7");
                table.HasCheckConstraint(
                    "ck_work_schedule_days_working_state",
                    "(is_working_day AND work_time_type IN ('fixed', 'flexible') " +
                    "AND break_type IN ('none', 'fixed', 'flexible') " +
                    "AND expected_work_area IN ('onsite', 'remote', 'either', 'field')) OR " +
                    "(NOT is_working_day AND work_time_type IS NULL AND break_type IS NULL " +
                    "AND expected_work_area IS NULL)");
                table.HasCheckConstraint(
                    "ck_work_schedule_days_work_time",
                    "(work_time_type = 'fixed' AND start_time IS NOT NULL AND end_time IS NOT NULL " +
                    "AND required_work_minutes IS NULL) OR " +
                    "(work_time_type = 'flexible' AND required_work_minutes BETWEEN 1 AND 1440 " +
                    "AND start_time IS NULL AND end_time IS NULL) OR work_time_type IS NULL");
                table.HasCheckConstraint(
                    "ck_work_schedule_days_break",
                    "(break_type = 'none' AND break_start_time IS NULL AND break_end_time IS NULL " +
                    "AND break_duration_minutes IS NULL) OR " +
                    "(break_type = 'fixed' AND break_start_time IS NOT NULL AND break_end_time IS NOT NULL " +
                    "AND break_duration_minutes IS NULL) OR " +
                    "(break_type = 'flexible' AND break_duration_minutes BETWEEN 1 AND 720 " +
                    "AND break_start_time IS NULL AND break_end_time IS NULL) OR break_type IS NULL");
            });
        builder.HasKey(day => day.Id);

        builder.Property(day => day.WorkTimeType).HasMaxLength(20);
        builder.Property(day => day.BreakType).HasMaxLength(20);
        builder.Property(day => day.ExpectedWorkArea).HasMaxLength(10);

        builder.HasIndex(day => new { day.TenantId, day.WorkScheduleId, day.DayOfWeek })
            .IsUnique();

        builder.HasOne<WorkSchedule>()
            .WithMany()
            .HasForeignKey(day => day.WorkScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
