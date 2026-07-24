using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.TimeAttendance;

public sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> builder)
    {
        builder.ToTable(
            "work_schedules",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_work_schedules_timezone",
                    "length(trim(timezone)) > 0");
                table.HasCheckConstraint(
                    "ck_work_schedules_public_holiday_country",
                    "NOT pull_public_holidays OR country_code IS NOT NULL");
            });
        builder.HasKey(schedule => schedule.Id);

        builder.Property(schedule => schedule.Name).HasMaxLength(100).IsRequired();
        builder.Property(schedule => schedule.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(schedule => schedule.Timezone).HasMaxLength(50).IsRequired();

        builder.HasIndex(schedule => new
        {
            schedule.TenantId,
            schedule.LegalEntityId,
            schedule.IsActive
        });
        builder.HasIndex(schedule => new { schedule.TenantId, schedule.LegalEntityId, schedule.Name });

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(schedule => schedule.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
