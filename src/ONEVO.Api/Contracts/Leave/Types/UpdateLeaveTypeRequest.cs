namespace ONEVO.Api.Contracts.Leave.Types;

public record UpdateLeaveTypeRequest(
    string Name,
    string? Description,
    string Color,
    bool IsPaid,
    string ApplicableGender,
    bool RequiresDocument,
    decimal? DocumentRequiredAfterHours,
    string[] AcceptedDocumentTypes);
