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
