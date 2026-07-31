namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public record TrayAuthResponseDto(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    int RefreshExpiresInSeconds);
