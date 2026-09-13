namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public record WorkModeResponse(
    Guid Id,
    Guid LegalEntityId,
    string Name,
    bool BiometricEnabled,
    bool WebEnabled,
    bool TrayEnabled,
    bool PhotoRequired,
    bool IsSystemSeeded,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
