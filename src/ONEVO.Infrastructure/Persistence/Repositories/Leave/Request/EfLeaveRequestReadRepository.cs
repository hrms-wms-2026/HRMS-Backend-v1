using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;

public sealed class EfLeaveRequestReadRepository(ApplicationDbContext db) : ILeaveRequestReadRepository
{
    public Task<IReadOnlyList<LeaveRequest>> ListApprovedCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default) =>
        ListCoveringAsync(tenantId, employeeIds, from, to, Covering.Approved, ct);

    public Task<IReadOnlyList<LeaveRequest>> ListPendingCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default) =>
        ListCoveringAsync(tenantId, employeeIds, from, to, Covering.Pending, ct);

    public Task<IReadOnlyList<LeaveRequest>> ListPartiallyCancelledCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default) =>
        ListCoveringAsync(tenantId, employeeIds, from, to, Covering.PartiallyCancelled, ct);

    private enum Covering { Approved, Pending, PartiallyCancelled }

    private async Task<IReadOnlyList<LeaveRequest>> ListCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        Covering covering,
        CancellationToken ct)
    {
        var partiallyCancelled = covering == Covering.PartiallyCancelled;
        var status = covering == Covering.Pending ? LeaveRequestStatuses.Pending : LeaveRequestStatuses.Approved;
        if (employeeIds.Count == 0 || from > to)
            return Array.Empty<LeaveRequest>();

        var fromStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return await db.LeaveRequests
            .AsNoTracking()
            .Where(request => request.TenantId == tenantId
                && employeeIds.Contains(request.EmployeeId)
                && (partiallyCancelled
                    ? request.Status == LeaveRequestStatuses.Cancelled && request.PartialCancelEffectiveDate != null
                    : request.Status == status)
                && request.StartAt < toExclusive
                && request.EndAt > fromStart)
            .OrderBy(request => request.EmployeeId)
            .ThenBy(request => request.StartAt)
            .ThenBy(request => request.EndAt)
            .ToListAsync(ct);
    }
}
