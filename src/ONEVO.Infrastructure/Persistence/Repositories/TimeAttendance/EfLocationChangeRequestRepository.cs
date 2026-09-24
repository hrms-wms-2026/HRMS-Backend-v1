using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfLocationChangeRequestRepository(ApplicationDbContext db) : ILocationChangeRequestRepository
{
    public Task AddAsync(LocationChangeRequest request, CancellationToken ct = default)
        => db.LocationChangeRequests.AddAsync(request, ct).AsTask();

    public Task<LocationChangeRequest?> GetTrackedByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
        => db.LocationChangeRequests.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<LocationChangeRequest?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
        => db.LocationChangeRequests.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<LocationChangeRequest?> GetActiveForEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.LocationChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId
            && (x.Status == LocationChangeRequest.StatusPending || x.Status == LocationChangeRequest.StatusApproved), ct);

    public Task<LocationChangeRequest?> GetTrackedApprovedUnappliedForEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.LocationChangeRequests.SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId
            && x.Status == LocationChangeRequest.StatusApproved, ct);

    public async Task<(IReadOnlyList<LocationChangeRequest> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var query = db.LocationChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId);
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
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => await db.LocationChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && x.Status == LocationChangeRequest.StatusPending)
            .Select(x => x.EmployeeId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<LocationChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        int skip, int take, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return (Array.Empty<LocationChangeRequest>(), 0);

        var query = db.LocationChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && employeeIds.Contains(x.EmployeeId)
                && x.Status == LocationChangeRequest.StatusPending);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<bool> HasActiveAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.LocationChangeRequests.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId
            && (x.Status == LocationChangeRequest.StatusPending || x.Status == LocationChangeRequest.StatusApproved), ct);

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
