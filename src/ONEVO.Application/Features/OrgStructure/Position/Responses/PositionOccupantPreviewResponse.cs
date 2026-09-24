namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// avatarUrl is always null today: no safe tenant-authenticated file-serving endpoint exists yet
// for file_records (confirmed - no such controller anywhere in this codebase). Returning
// avatarFileId lets the frontend show initials/placeholders now and swap in real images once
// that endpoint exists, without another contract change.
public record PositionOccupantPreviewResponse(
    Guid EmployeeId,
    string DisplayName,
    string Initials,
    Guid? AvatarFileId,
    string? AvatarUrl);
