namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ApplicationUsageDto(
    Guid Id,
    string ProcessName,
    string ApplicationName,
    string? ApplicationCategory,
    int TotalSeconds,
    bool? IsProductive,
    bool? IsAllowed);
