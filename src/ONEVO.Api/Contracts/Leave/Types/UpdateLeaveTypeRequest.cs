namespace ONEVO.Api.Contracts.Leave.Types;

public record UpdateLeaveTypeRequest(
    string Name,
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
    int MinimumNoticeDays);
