using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public sealed class EfSeatIncreaseRequestRepository(ApplicationDbContext db) : ISeatIncreaseRequestRepository
{
    public Task AddAsync(SeatIncreaseRequest request, CancellationToken ct = default)
        => db.SeatIncreaseRequests.AddAsync(request, ct).AsTask();

    public Task<SeatIncreaseRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.SeatIncreaseRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<bool> AnyPendingAsync(Guid tenantId, CancellationToken ct = default)
        => db.SeatIncreaseRequests.AnyAsync(x => x.TenantId == tenantId && x.Status == SeatIncreaseRequestStatus.Pending, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class EfOnboardingInboxRepository(ApplicationDbContext db) : IOnboardingInboxRepository
{
    private const string AccessPending = "Pending";

    public async Task<IReadOnlyList<OnboardingInboxRow>> ListAsync(
        Guid tenantId, Guid userId, bool positionApprover, bool seatApprover, DateTimeOffset decidedSince, CancellationToken ct = default)
    {
        var access = db.AccessGrantRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.OnboardingDraftId != null
            && ((positionApprover && x.ApprovalStatus == AccessPending)
                || x.RequestedByUserId == userId
                || (x.DecidedByUserId == userId && x.DecidedAt >= decidedSince)));
        var seats = db.SeatIncreaseRequests.AsNoTracking().Where(x => x.TenantId == tenantId
            && ((seatApprover && x.Status == SeatIncreaseRequestStatus.Pending)
                || x.RequestedByUserId == userId
                || (x.DecidedByUserId == userId && x.DecidedAt >= decidedSince)));
        return [.. await AccessRowsAsync(tenantId, access, ct), .. await SeatRowsAsync(tenantId, seats, ct)];
    }

    public async Task<OnboardingInboxRow?> GetAsync(Guid tenantId, string kind, Guid id, CancellationToken ct = default)
    {
        var rows = kind switch
        {
            OnboardingInboxRowKinds.PositionApproval => await AccessRowsAsync(tenantId,
                db.AccessGrantRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == id && x.OnboardingDraftId != null), ct),
            OnboardingInboxRowKinds.SeatIncrease => await SeatRowsAsync(tenantId,
                db.SeatIncreaseRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == id), ct),
            _ => [],
        };
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<OnboardingInboxRow>> AccessRowsAsync(
        Guid tenantId, IQueryable<AccessGrantRequest> requests, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking().Where(u => u.TenantId == tenantId);
        var rows = await (
            from x in requests
            join draft in db.OnboardingDrafts.AsNoTracking() on x.OnboardingDraftId equals draft.Id into draftJoin
            from draft in draftJoin.DefaultIfEmpty()
            join position in db.Positions.AsNoTracking() on x.TargetPositionId equals position.Id into positionJoin
            from position in positionJoin.DefaultIfEmpty()
            join department in db.Departments.AsNoTracking() on x.TargetDepartmentId equals department.Id into departmentJoin
            from department in departmentJoin.DefaultIfEmpty()
            join requester in users on x.RequestedByUserId equals requester.Id into requesterJoin
            from requester in requesterJoin.DefaultIfEmpty()
            join decider in users on x.DecidedByUserId equals decider.Id into deciderJoin
            from decider in deciderJoin.DefaultIfEmpty()
            select new
            {
                x,
                SubjectName = draft != null ? draft.FirstName + " " + draft.LastName : null,
                LegalEntityId = draft != null ? (Guid?)draft.LegalEntityId : null,
                StartDate = draft != null ? draft.StartDate : null,
                PositionName = position != null ? position.Name : null,
                DepartmentName = department != null ? department.Name : null,
                RequesterName = requester != null ? requester.FirstName + " " + requester.LastName : null,
                DeciderName = decider != null ? decider.FirstName + " " + decider.LastName : null,
            }).ToListAsync(ct);

        var requesterEmployees = await EmployeeByUserAsync(tenantId, rows.Select(r => r.x.RequestedByUserId), ct);
        return rows.Select(r => new OnboardingInboxRow(
            OnboardingInboxRowKinds.PositionApproval, r.x.Id, r.x.ApprovalStatus, r.x.RequestedByUserId,
            Name(r.RequesterName, "Someone"), requesterEmployees.GetValueOrDefault(r.x.RequestedByUserId),
            r.x.DecidedByUserId, r.DeciderName?.Trim(), r.x.RequestedAt, r.x.DecidedAt, r.x.DecisionNote,
            r.SubjectName?.Trim(), r.LegalEntityId, r.PositionName, r.DepartmentName, r.StartDate,
            null, null, r.x.ChangeReason)).ToList();
    }

    private async Task<IReadOnlyList<OnboardingInboxRow>> SeatRowsAsync(
        Guid tenantId, IQueryable<SeatIncreaseRequest> requests, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking().Where(u => u.TenantId == tenantId);
        var rows = await (
            from x in requests
            join draft in db.OnboardingDrafts.AsNoTracking() on x.OnboardingDraftId equals draft.Id into draftJoin
            from draft in draftJoin.DefaultIfEmpty()
            join requester in users on x.RequestedByUserId equals requester.Id into requesterJoin
            from requester in requesterJoin.DefaultIfEmpty()
            join decider in users on x.DecidedByUserId equals decider.Id into deciderJoin
            from decider in deciderJoin.DefaultIfEmpty()
            select new
            {
                x,
                SubjectName = draft != null ? draft.FirstName + " " + draft.LastName : null,
                LegalEntityId = draft != null ? (Guid?)draft.LegalEntityId : null,
                RequesterName = requester != null ? requester.FirstName + " " + requester.LastName : null,
                DeciderName = decider != null ? decider.FirstName + " " + decider.LastName : null,
            }).ToListAsync(ct);

        var requesterEmployees = await EmployeeByUserAsync(tenantId, rows.Select(r => r.x.RequestedByUserId), ct);
        var waiting = rows.Any(r => r.x.Status == SeatIncreaseRequestStatus.Pending)
            ? await ListWaitingForSeatNamesAsync(tenantId, ct)
            : [];
        return rows.Select(r =>
        {
            var pending = r.x.Status == SeatIncreaseRequestStatus.Pending;
            return new OnboardingInboxRow(
                OnboardingInboxRowKinds.SeatIncrease, r.x.Id, r.x.Status, r.x.RequestedByUserId,
                Name(r.RequesterName, "Someone"), requesterEmployees.GetValueOrDefault(r.x.RequestedByUserId),
                r.x.DecidedByUserId, r.DeciderName?.Trim(), r.x.RequestedAt, r.x.DecidedAt, r.x.DecisionNote,
                pending && waiting.Count > 0 ? People(waiting) : r.SubjectName?.Trim(), r.LegalEntityId, null, null, null,
                pending ? Math.Max(r.x.RequestedSeats, waiting.Count) : r.x.RequestedSeats, r.x.SeatsAtRequest, r.x.Reason);
        }).ToList();
    }

    private static string People(IReadOnlyList<string> names)
        => names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more";

    public async Task<IReadOnlyList<string>> ListWaitingForSeatNamesAsync(Guid tenantId, CancellationToken ct = default)
        => (await db.OnboardingDrafts.AsNoTracking()
                .Where(d => d.TenantId == tenantId
                    && (d.Status == OnboardingDraftStatus.WaitingForSeat || d.Status == OnboardingDraftStatus.WaitingForPositionApproval))
                .OrderBy(d => d.CreatedAt)
                .Select(d => d.FirstName + " " + d.LastName)
                .ToListAsync(ct))
            .Select(n => n.Trim()).ToList();

    /// <summary>Each requester's employee record (for the avatar), oldest first when a user has several.</summary>
    private async Task<Dictionary<Guid, Guid?>> EmployeeByUserAsync(Guid tenantId, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.UserId))
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { e.UserId, e.Id })
            .ToListAsync(ct);
        return employees.GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => (Guid?)g.First().Id);
    }

    private static string Name(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
