using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingDelegateRepository : IMeetingDelegateRepository
{
    // The same "active employee" set the meeting create applier uses.
    private static readonly int[] ActiveStatuses = [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly ApplicationDbContext _db;

    public EfMeetingDelegateRepository(ApplicationDbContext db) => _db = db;

    public void Add(MeetingDelegate row) => _db.MeetingDelegates.Add(row);

    public Task<MeetingDelegate?> GetUnrevokedTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => _db.MeetingDelegates.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id && d.RevokedAt == null, ct);

    public Task<MeetingDelegate?> GetUnrevokedTrackedForPairAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId,
        CancellationToken ct = default)
        => _db.MeetingDelegates.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.RevokedAt == null
            && d.PrincipalEmployeeId == principalEmployeeId && d.DelegateEmployeeId == delegateEmployeeId, ct);

    public async Task<IReadOnlyList<MeetingDelegate>> ListUnrevokedForPrincipalAsync(Guid tenantId, Guid principalEmployeeId,
        CancellationToken ct = default)
        => await _db.MeetingDelegates.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.PrincipalEmployeeId == principalEmployeeId && d.RevokedAt == null)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListActiveDelegateIdsAsync(Guid tenantId, Guid principalEmployeeId, DateTimeOffset now,
        CancellationToken ct = default)
        => await Active(tenantId, now).Where(d => d.PrincipalEmployeeId == principalEmployeeId)
            .Select(d => d.DelegateEmployeeId).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListActivePrincipalIdsAsync(Guid tenantId, Guid delegateEmployeeId, DateTimeOffset now,
        CancellationToken ct = default)
        => await Active(tenantId, now).Where(d => d.DelegateEmployeeId == delegateEmployeeId)
            .Select(d => d.PrincipalEmployeeId).Distinct().ToListAsync(ct);

    public Task<bool> IsActiveDelegateAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, DateTimeOffset now,
        CancellationToken ct = default)
        => Active(tenantId, now).AnyAsync(d => d.PrincipalEmployeeId == principalEmployeeId && d.DelegateEmployeeId == delegateEmployeeId, ct);

    private IQueryable<MeetingDelegate> Active(Guid tenantId, DateTimeOffset now)
        => _db.MeetingDelegates.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.RevokedAt == null && (d.ExpiresAt == null || d.ExpiresAt > now)
                && _db.Employees.Any(e => e.Id == d.PrincipalEmployeeId && ActiveStatuses.Contains(e.EmploymentStatusId))
                && _db.Employees.Any(e => e.Id == d.DelegateEmployeeId && ActiveStatuses.Contains(e.EmploymentStatusId)));
}
