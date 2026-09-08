using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfExternalCalendarEventLinkRepository : IExternalCalendarEventLinkRepository
{
    private readonly ApplicationDbContext _db;

    public EfExternalCalendarEventLinkRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ExternalCalendarEventLink link, CancellationToken ct = default)
        => await _db.ExternalCalendarEventLinks.AddAsync(link, ct);

    public async Task<ExternalCalendarEventLink?> GetTrackedByConnectionAndExternalEventAsync(Guid tenantId, Guid connectionId, string externalEventId, CancellationToken ct = default)
        => await _db.ExternalCalendarEventLinks.FirstOrDefaultAsync(
            l => l.TenantId == tenantId && l.ExternalCalendarConnectionId == connectionId && l.ExternalEventId == externalEventId, ct);

    public async Task<ExternalCalendarEventLink?> GetTrackedByCalendarEventAndConnectionAsync(Guid tenantId, Guid calendarEventId, Guid connectionId, CancellationToken ct = default)
        => await _db.ExternalCalendarEventLinks.FirstOrDefaultAsync(
            l => l.TenantId == tenantId && l.CalendarEventId == calendarEventId && l.ExternalCalendarConnectionId == connectionId, ct);

    public async Task<IReadOnlyList<ExternalCalendarEventLink>> GetByConnectionIdAsync(Guid tenantId, Guid connectionId, CancellationToken ct = default)
        => await _db.ExternalCalendarEventLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ExternalCalendarConnectionId == connectionId)
            .ToListAsync(ct);

    public void Update(ExternalCalendarEventLink link) => _db.ExternalCalendarEventLinks.Update(link);
    public void Remove(ExternalCalendarEventLink link) => _db.ExternalCalendarEventLinks.Remove(link);
    public void RemoveRange(IEnumerable<ExternalCalendarEventLink> links) => _db.ExternalCalendarEventLinks.RemoveRange(links);
}
