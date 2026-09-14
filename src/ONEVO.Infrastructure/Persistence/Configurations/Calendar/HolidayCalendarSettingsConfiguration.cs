using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class HolidayCalendarSettingsConfiguration : IEntityTypeConfiguration<HolidayCalendarSettings>
{
    public void Configure(EntityTypeBuilder<HolidayCalendarSettings> builder)
    {
        builder.ToTable("holiday_calendar_settings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DefaultCountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.OverrideCountryCode).HasMaxLength(2);
        builder.Property(x => x.Provider).HasMaxLength(30).IsRequired().HasDefaultValue(HolidayCalendarProviders.NagerHolidays);
        builder.Ignore(x => x.EffectiveCountryCode);

        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId })
            .IsUnique()
            .HasDatabaseName("ix_holiday_calendar_settings_one_per_legal_entity");
    }
}
