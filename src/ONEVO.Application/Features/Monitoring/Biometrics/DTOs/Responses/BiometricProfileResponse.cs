namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record BiometricProfileResponse(Guid ProfileId, string Status, DateTimeOffset EnrolledAt);
