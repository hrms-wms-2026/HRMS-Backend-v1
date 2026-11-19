using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;

/// <summary>Today's tasks: assigned tasks due today or created/updated today. "Today" is the employee's
/// legal-entity local day (UTC when none is set), same as the activity-by-hour card.</summary>
public sealed class GetEmployeeRecentTasksQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ILegalEntityRepository legalEntities,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeRecentTasksQuery, Result<EmployeeRecentTasksResponse>>
{
    public async Task<Result<EmployeeRecentTasksResponse>> Handle(GetEmployeeRecentTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeRecentTasksResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var legalEntity = access.Value!.LegalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct)
            : null;
        var zone = AttendancePeriodCalculator.ResolveTimezone(legalEntity?.Timezone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        var window = AttendanceTodayStateService.GetLocalDayWindow(today, zone);

        var rows = await tasks.ListTodayAssignedAsync(
            tenantId, request.EmployeeId, today, window.Start, window.End, WorkActivityTaskRules.RecentTasksMaxItems, ct);
        return Result<EmployeeRecentTasksResponse>.Success(new EmployeeRecentTasksResponse(rows.Select(r => WorkActivityTaskRules.ToItem(r, today, window.Start, window.End)).ToList()));
    }
}
