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
    // Time & Attendance work_schedules/work_schedule_days feature). Both null
    // or both set. Overnight windows are allowed: WorkEndTime <= WorkStartTime
    // means the end is on the next calendar day.
    public TimeOnly? WorkStartTime { get; set; }
    public TimeOnly? WorkEndTime { get; set; }

    // Default company break duration in minutes. Independent of WorkStartTime/
    // WorkEndTime - null means not configured; when set, must be >= 0.
    public int? BreakDurationMinutes { get; set; }

    // Office location, used only to flag (never block) an on-site employee whose
    // check-in location falls outside the applicable ClockInPolicy's AllowedRadiusMeters of
    // this point (the radius itself lives on ClockInPolicy, not here, so there is exactly one
    // "how far is allowed" setting shared by both the onsite and remote location checks). Null
    // = not configured, meaning the on-site location warning never fires for this legal entity.
    // Latitude/Longitude are set together or not at all; OfficeAddress is a free-text display
    // label only, not used in the distance calculation.
    public string? OfficeAddress { get; set; }
    public double? OfficeLatitude { get; set; }
    public double? OfficeLongitude { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
