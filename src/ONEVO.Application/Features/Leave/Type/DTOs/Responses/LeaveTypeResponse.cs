namespace ONEVO.Application.Features.Leave.Type.DTOs.Responses;

public record LeaveTypeResponse(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays,
    bool IsActive,
    DateTimeOffset CreatedAt);
