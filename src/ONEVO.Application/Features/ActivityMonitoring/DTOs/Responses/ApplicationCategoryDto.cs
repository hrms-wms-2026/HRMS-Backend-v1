namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ApplicationCategoryDto(
    Guid Id,
    string ApplicationNamePattern,
    string Category,
    bool? IsProductive);
