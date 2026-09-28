namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// The frontend resolves AvatarFileId through the tenant-authenticated /files/{id} endpoint.
// AvatarUrl remains only as a compatibility field for older clients and is currently null.
public record PositionOccupantPreviewResponse(
    Guid EmployeeId,
    string DisplayName,
    string Initials,
    Guid? AvatarFileId,
    string? AvatarUrl);
