namespace ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;

public record ExceptionDto(
    Guid Id, Guid EmployeeId, string Type, string Status, string Title, string Description,
    DateTimeOffset DetectedAt, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt, DateTimeOffset? EscalatedAt);
