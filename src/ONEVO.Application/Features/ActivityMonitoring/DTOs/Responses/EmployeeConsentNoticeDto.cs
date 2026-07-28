namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record EmployeeConsentNoticeDto(
    Guid IncidentId,
    DateTimeOffset OccurredAt,
    string Decision);
