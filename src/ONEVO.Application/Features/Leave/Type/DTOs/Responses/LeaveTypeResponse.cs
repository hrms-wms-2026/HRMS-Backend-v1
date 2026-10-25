namespace ONEVO.Application.Features.Leave.Type.DTOs.Responses;

public record LeaveTypeResponse(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    string Color,
    bool IsPaid,
    string ApplicableGender,
    bool RequiresDocument,
    decimal? DocumentRequiredAfterHours,
    string[] AcceptedDocumentTypes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int PolicyCount = 0);
