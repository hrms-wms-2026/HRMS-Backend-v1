using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfWorkAreaChangeRequestRepository(ApplicationDbContext db)
    : IWorkAreaChangeRequestRepository
{
    public Task AddAsync(WorkAreaChangeRequest request, CancellationToken ct = default)
        => db.WorkAreaChangeRequests.AddAsync(request, ct).AsTask();

    public Task<WorkAreaChangeRequest?> GetTrackedByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
        => db.WorkAreaChangeRequests.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<WorkAreaChangeRequest?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
        => db.WorkAreaChangeRequests.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task<(IReadOnlyList<WorkAreaChangeRequest> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        int skip, int take, CancellationToken ct = default)
    {
        var query = db.WorkAreaChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId);
        if (from is not null)
            query = query.Where(x => x.Date >= from.Value);
        if (to is not null)
            query = query.Where(x => x.Date <= to.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status.Trim().ToLowerInvariant());

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, DateOnly? from, DateOnly? to,
        CancellationToken ct = default)
    {
        var query = db.WorkAreaChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.LegalEntityId == legalEntityId
                && x.EmployeeId != Guid.Empty
                && x.Status == WorkAreaChangeRequest.StatusPending);
        if (from is not null)
            query = query.Where(x => x.Date >= from.Value);
        if (to is not null)
            query = query.Where(x => x.Date <= to.Value);

        return await query
            .Select(x => x.EmployeeId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<WorkAreaChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        DateOnly? from, DateOnly? to, int skip, int take, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return (Array.Empty<WorkAreaChangeRequest>(), 0);

        var query = db.WorkAreaChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.LegalEntityId == legalEntityId
                && x.EmployeeId != Guid.Empty
                && employeeIds.Contains(x.EmployeeId)
                && x.Status == WorkAreaChangeRequest.StatusPending);
        if (from is not null)
            query = query.Where(x => x.Date >= from.Value);
        if (to is not null)
            query = query.Where(x => x.Date <= to.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<bool> HasActiveForDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default)
        => db.WorkAreaChangeRequests.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId
            && x.EmployeeId == employeeId
            && x.Date == date
            && (x.Status == WorkAreaChangeRequest.StatusPending
                || x.Status == WorkAreaChangeRequest.StatusApproved), ct);

    public async Task<WorkAreaChangeRequest?> GetApprovedForDateAsync(
        Guid tenantId, Guid legalEntityId, Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        var matches = await db.WorkAreaChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.LegalEntityId == legalEntityId
                && x.EmployeeId == employeeId
                && x.Date == date
                && x.Status == WorkAreaChangeRequest.StatusApproved)
            .ToListAsync(ct);

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InconsistentWorkAreaChangeRequestStateException()
        };
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }
}
