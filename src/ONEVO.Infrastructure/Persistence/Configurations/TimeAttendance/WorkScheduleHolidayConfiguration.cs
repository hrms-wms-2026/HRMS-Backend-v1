using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class WorkScheduleHolidayConfiguration
    : IEntityTypeConfiguration<WorkScheduleHoliday>
{
    public void Configure(EntityTypeBuilder<WorkScheduleHoliday> builder)
    {
        builder.ToTable(
            "work_schedule_holidays",
            table => table.HasCheckConstraint(
                "ck_work_schedule_holidays_source",
                "source IN ('country_public_holiday', 'manual')"));
        builder.HasKey(holiday => holiday.Id);

        builder.Property(holiday => holiday.Name).HasMaxLength(100).IsRequired();
        builder.Property(holiday => holiday.Source).HasMaxLength(30).IsRequired();

        builder.HasIndex(holiday => new
        {
            holiday.TenantId,
            holiday.WorkScheduleId,
            holiday.Date
        }).IsUnique();

        builder.HasOne<WorkSchedule>()
            .WithMany()
            .HasForeignKey(holiday => holiday.WorkScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(holiday => holiday.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
