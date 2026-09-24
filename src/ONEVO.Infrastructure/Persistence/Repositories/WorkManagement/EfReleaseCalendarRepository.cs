using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfReleaseCalendarRepository : IReleaseCalendarRepository
{
    private readonly ApplicationDbContext _db;

    public EfReleaseCalendarRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ReleaseCalendarEntry entry, CancellationToken ct = default)
    {
        await _db.ReleaseCalendarEntries.AddAsync(entry, ct);
    }
}
