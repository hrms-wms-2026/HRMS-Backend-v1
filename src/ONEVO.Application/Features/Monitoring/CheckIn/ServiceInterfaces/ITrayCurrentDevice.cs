namespace ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

public interface ITrayCurrentDevice
{
    Guid DeviceRegistrationId { get; }
    Guid UserId { get; }
    Guid TenantId { get; }
    bool IsAuthenticated { get; }
}
