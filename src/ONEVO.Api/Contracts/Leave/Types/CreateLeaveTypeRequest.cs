namespace ONEVO.Api.Contracts.Leave.Types;

public record CreateLeaveTypeRequest(
    string Name,
    string Code,
    string? Description,
    string Color,
    bool IsPaid,
    string ApplicableGender,
    bool RequiresDocument,
    decimal? DocumentRequiredAfterHours,
    string[] AcceptedDocumentTypes);
