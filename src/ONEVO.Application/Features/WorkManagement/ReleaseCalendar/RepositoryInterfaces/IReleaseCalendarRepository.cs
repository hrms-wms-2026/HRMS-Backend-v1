using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

namespace ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;

public interface IReleaseCalendarRepository
{
    Task AddAsync(ReleaseCalendarEntry entry, CancellationToken ct = default);
}
