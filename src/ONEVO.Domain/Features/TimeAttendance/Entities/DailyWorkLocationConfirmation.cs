using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

/// <summary>
/// One row per employee per calendar day: which of the three approved work-location options
/// they picked on the tray's daily confirm screen, and the GPS fix captured at that moment (null
/// when location tracking is off for the tenant). Re-confirming the same day overwrites the row.
/// Read by LocationRuleEvaluatorJob to know which reference point (office vs. home/other) applies
/// today, and by ConfirmWorkLocationCommandHandler to auto-register EmployeeWorkLocation the
/// first time an employee ever confirms Home/Other.
/// </summary>
public sealed class DailyWorkLocationConfirmation : ITenantOwnedEntity
{
    public const string LocationTypeOffice = "office";
    public const string LocationTypeHome = "home";
    public const string LocationTypeOther = "other";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
}
