namespace ONEVO.Application.Features.CoreHr.PositionAssignment.Models;

public sealed record PositionOccupancyPreview(
    int AssignedCount,
    IReadOnlyList<PositionOccupantPreviewItem> OccupantPreview,
    // Seats held by "planned" assignments - an invited employee who hasn't accepted yet. They
    // aren't occupants, but TryReservePositionAssignmentAsync counts them against MaxOccupancy,
    // so anything showing free seats must subtract them too.
    int ReservedCount = 0);

public sealed record PositionOccupantPreviewItem(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    Guid? AvatarFileId);

public sealed record PositionActiveHolder(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string WorkEmail,
    Guid? AvatarFileId,
    string? DepartmentName = null);

/// <summary>Active primary-seat holders of a position, including the UserId required by
/// employee_checklist_tasks.assigned_to_id (FK to users). Distinct from PositionActiveHolder,
/// which is employee-id identity for reporting-manager picks.</summary>
public sealed record ChecklistAssignee(
    Guid EmployeeId,
    Guid UserId,
    string DisplayName,
    string WorkEmail,
    Guid? AvatarFileId);
