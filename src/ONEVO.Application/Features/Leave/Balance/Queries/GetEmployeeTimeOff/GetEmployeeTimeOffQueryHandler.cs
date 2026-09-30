using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;

public sealed class GetEmployeeTimeOffQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    ILeaveEntitlementRepository entitlements,
    ILeavePolicyRepository policies,
    ILeaveRequestReadRepository leaveRequests,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeTimeOffQuery, Result<EmployeeTimeOffResponse>>
{
    public const string ModulePermission = "leave:read";
    private const int UpcomingWindowDays = 90;

    public async Task<Result<EmployeeTimeOffResponse>> Handle(GetEmployeeTimeOffQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeTimeOffResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeTimeOffResponse>.Forbidden("You do not have access to this employee's time off.");

        var today = clock.Today;
        var year = request.Year ?? today.Year;
        if (year < 2000 || year > 2100)
            return Result<EmployeeTimeOffResponse>.Failure("year must be between 2000 and 2100.");

        var rows = await entitlements.ListRowsAsync(
            tenantId,
            new LeaveEntitlementListFilter(year, request.EmployeeId, null, null, null, null, null, null),
            ct);
        var mapped = await LeaveBalanceMapping.MapAsync(
            policies, tenantId, year, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), rows, ct);

        var balances = mapped
            .Select(b => new EmployeeTimeOffBalance(
                b.LeaveTypeId, b.LeaveTypeName, b.LeaveTypeCode,
                b.EntitledHours, b.UsedHours, b.PendingHours, b.RemainingHours, b.IsNegative))
            .ToList();

        var upcoming = await leaveRequests.ListApprovedCoveringAsync(
            tenantId, [request.EmployeeId], today, today.AddDays(UpcomingWindowDays), ct);
        var next = upcoming
            .Where(l => DateOnly.FromDateTime(l.EndAt.UtcDateTime) >= today)
            .OrderBy(l => l.StartAt)
            .FirstOrDefault();

        var typeNames = mapped.GroupBy(b => b.LeaveTypeId).ToDictionary(g => g.Key, g => g.First().LeaveTypeName);
        var nextLeave = next is null
            ? null
            : new EmployeeUpcomingLeave(
                next.LeaveTypeId,
                typeNames.GetValueOrDefault(next.LeaveTypeId),
                DateOnly.FromDateTime(next.StartAt.UtcDateTime),
                DateOnly.FromDateTime(next.EndAt.UtcDateTime),
                next.TotalHours);

        return Result<EmployeeTimeOffResponse>.Success(new EmployeeTimeOffResponse(year, balances, nextLeave));
    }
}
