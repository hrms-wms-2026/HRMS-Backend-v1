using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyActiveTasks;

public sealed class GetMyActiveTasksQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    ICallerIdentityResolver identity,
    IWorkTaskRepository tasks)
    : IRequestHandler<GetMyActiveTasksQuery, Result<MyTasksResponse>>
{
    public async Task<Result<MyTasksResponse>> Handle(GetMyActiveTasksQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<MyTasksResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employeeId = await identity.ResolveCallerEmployeeIdAsync(tenantId, currentUser.UserId, ct);
        if (employeeId is null)
            return Result<MyTasksResponse>.Forbidden("No employee record for the current user.");

        var today = DateOnly.FromDateTime(dateTime.UtcNow.UtcDateTime);
        var upcomingCutoff = today.AddDays(Math.Max(0, request.UpcomingDays));

        var rows = await tasks.GetMyActiveTasksAsync(tenantId, employeeId.Value, upcomingCutoff, ct);

        var items = rows
            .Select(r => new MyTaskItem(
                r.Id, r.ShortId, r.Title, r.DueDate, r.DueDate < today,
                r.ProjectId, r.ProjectName, r.ObjectiveId, r.Priority))
            .ToList();

        return Result<MyTasksResponse>.Success(new MyTasksResponse(items));
    }
}
