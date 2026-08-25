using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;

public sealed class EfLeaveRequestReadRepository(ApplicationDbContext db) : ILeaveRequestReadRepository
{
    public async Task<IReadOnlyList<LeaveRequest>> ListApprovedCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0 || from > to)
            return Array.Empty<LeaveRequest>();

        return await db.LeaveRequests
            .AsNoTracking()
            .Where(request => request.TenantId == tenantId
                && employeeIds.Contains(request.EmployeeId)
                && request.Status == LeaveRequestStatuses.Approved
                && request.StartDate <= to
                && request.EndDate >= from)
            .OrderBy(request => request.EmployeeId)
            .ThenBy(request => request.StartDate)
            .ThenBy(request => request.EndDate)
            .ToListAsync(ct);
    }
}
