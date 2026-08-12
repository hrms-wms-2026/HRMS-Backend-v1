using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.OrgStructure.Entities;

public class LegalEntity : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public bool IsPrimary { get; set; } = true;

    public string? CompanyCode { get; set; }
    public Guid? LogoFileId { get; set; }
    public Guid? ParentLegalEntityId { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? VatGstNumber { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }
    public string? Timezone { get; set; }
    public int FinancialYearStartMonth { get; set; } = 1;

    // 1=Monday .. 7=Sunday.
    public int FirstDayOfWeek { get; set; } = 1;

    // JSON array of weekday numbers (1=Monday..7=Sunday), e.g. "[1,2,3,4,5]".
    public string StandardWorkingDays { get; set; } = "[1,2,3,4,5]";

    public string DefaultLanguage { get; set; } = "en-US";
    public string DateFormat { get; set; } = "DD MMM YYYY";

    // Fixed values only: "12h" or "24h".
    public string TimeFormat { get; set; } = "12h";

    // Default company working hours (Phase 1 stand-in ahead of the deferred
    // Time & Attendance work_schedules/work_schedule_days feature). Same-day
    // only - both null or both set with WorkStartTime < WorkEndTime.
    public TimeOnly? WorkStartTime { get; set; }
    public TimeOnly? WorkEndTime { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
