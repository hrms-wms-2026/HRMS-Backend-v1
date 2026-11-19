using Microsoft.EntityFrameworkCore;
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

    public async Task<IReadOnlyList<UpcomingReleaseRow>> ListForRecipientAsync(
        Guid tenantId, Guid recipientUserId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await (
            from entry in _db.ReleaseCalendarEntries.AsNoTracking()
            join version in _db.ProjectVersions.AsNoTracking() on entry.VersionId equals version.Id
            join project in _db.Projects.AsNoTracking() on entry.ProjectId equals project.Id
            where entry.TenantId == tenantId
                  && entry.RecipientUserId == recipientUserId
                  && entry.IsActive
                  && entry.ScheduledDate >= @from && entry.ScheduledDate <= to
            orderby entry.ScheduledDate
            select new UpcomingReleaseRow(entry.Id, entry.ScheduledDate, entry.ReminderType, entry.Notes, version.Name, project.Name)
        ).ToListAsync(ct);
    }
}
