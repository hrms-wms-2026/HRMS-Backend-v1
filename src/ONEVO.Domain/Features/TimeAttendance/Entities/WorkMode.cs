using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class WorkMode : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool BiometricEnabled { get; set; }
    public bool WebEnabled { get; set; }
    public bool TrayEnabled { get; set; }
    public bool PhotoRequired { get; set; }

    // Location behavior - independent toggles, not a fixed Onsite/Remote/Hybrid classification.
    // Neither set: employee's location is checked against the legal entity's configured office
    // point (LegalEntity.OfficeLatitude/OfficeLongitude). SelfRegistersLocation: their first
    // clock-in/check-in becomes their own permanent reference point instead (see
    // SubmitCheckInCommandHandler). AllowsDailyLocationChoice: skips both of the above and keeps
    // the daily office/home/other confirmation screen (ConfirmWorkLocationCommandHandler) for
    // employees whose work arrangement genuinely varies day to day.
    public bool SelfRegistersLocation { get; set; }
    public bool AllowsDailyLocationChoice { get; set; }

    // Provenance only - never blocks edit or deactivation. Shown in the UI as a "default" badge.
    public bool IsSystemSeeded { get; set; }

    public int DisplayOrder { get; set; }

    // Soft delete. Deactivated rows stay resolvable by FK from Employee/AttendanceRecord/
    // WorkAreaChangeRequest for historical integrity - see spec Component 1. Excluded from
    // the 5-per-legal-entity cap and from admin pickers.
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
