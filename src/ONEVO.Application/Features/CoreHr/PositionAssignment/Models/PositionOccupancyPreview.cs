namespace ONEVO.Application.Features.CoreHr.PositionAssignment.Models;

public sealed record PositionOccupancyPreview(
    int AssignedCount,
    IReadOnlyList<PositionOccupantPreviewItem> OccupantPreview);

public sealed record PositionOccupantPreviewItem(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    Guid? AvatarFileId);
