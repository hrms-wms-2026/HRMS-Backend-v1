using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

public interface IActivityRawBufferRepository
{
    Task AddAsync(ActivityRawBuffer buffer, CancellationToken ct);
}
