using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class HolidayCalendarProviders
{
    public const string NagerHolidays = "nager_holidays";
}

public class HolidayCalendarSettings : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public string DefaultCountryCode { get; set; } = string.Empty;
    public string? OverrideCountryCode { get; set; }
    public bool HolidaySyncEnabled { get; set; } = true;
    public string Provider { get; set; } = HolidayCalendarProviders.NagerHolidays;
    public int? LastSyncedYear { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public Guid? UpdatedById { get; set; }

    public string EffectiveCountryCode => OverrideCountryCode ?? DefaultCountryCode;
}
