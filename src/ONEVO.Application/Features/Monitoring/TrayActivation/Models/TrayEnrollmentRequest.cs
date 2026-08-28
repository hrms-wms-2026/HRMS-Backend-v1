namespace ONEVO.Application.Features.Monitoring.TrayActivation.Models;

public sealed record TrayEnrollmentRequest(
    Guid TenantId,
    Guid UserId,
    string DeviceName,
    string DeviceOs,
    string DeviceFingerprint);
