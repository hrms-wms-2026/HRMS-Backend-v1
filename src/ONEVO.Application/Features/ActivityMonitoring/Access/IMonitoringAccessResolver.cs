namespace ONEVO.Application.Features.ActivityMonitoring.Access;

public interface IMonitoringAccessResolver
{
    Task<bool> CanReadEmployeeAsync(
        Guid targetEmployeeId,
        CancellationToken ct = default);
}
