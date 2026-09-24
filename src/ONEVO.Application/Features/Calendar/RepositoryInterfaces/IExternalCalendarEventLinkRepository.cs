using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface IExternalCalendarEventLinkRepository
{
    Task AddAsync(ExternalCalendarEventLink link, CancellationToken ct = default);
    Task<ExternalCalendarEventLink?> GetTrackedByConnectionAndExternalEventAsync(Guid tenantId, Guid connectionId, string externalEventId, CancellationToken ct = default);
    Task<ExternalCalendarEventLink?> GetTrackedByCalendarEventAndConnectionAsync(Guid tenantId, Guid calendarEventId, Guid connectionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalCalendarEventLink>> GetByConnectionIdAsync(Guid tenantId, Guid connectionId, CancellationToken ct = default);
    void Update(ExternalCalendarEventLink link);
    void Remove(ExternalCalendarEventLink link);
    void RemoveRange(IEnumerable<ExternalCalendarEventLink> links);
}
