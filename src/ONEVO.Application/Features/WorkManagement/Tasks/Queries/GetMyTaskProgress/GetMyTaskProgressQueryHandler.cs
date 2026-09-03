using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskProgress;

public sealed class GetMyTaskProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    ICallerIdentityResolver identity,
    IWorkTaskRepository tasks)
    : IRequestHandler<GetMyTaskProgressQuery, Result<TaskProgressResponse>>
{
    public async Task<Result<TaskProgressResponse>> Handle(GetMyTaskProgressQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<TaskProgressResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employeeId = await identity.ResolveCallerEmployeeIdAsync(tenantId, currentUser.UserId, ct);
        if (employeeId is null)
            return Result<TaskProgressResponse>.Forbidden("No employee record for the current user.");

        var today = DateOnly.FromDateTime(dateTime.UtcNow.UtcDateTime);
        var rows = await tasks.GetMyTaskProgressRowsAsync(tenantId, employeeId.Value, ct);

        int completed = 0, overdue = 0, inProgress = 0, notStarted = 0;
        foreach (var row in rows)
        {
            // A task can also reach 100% progress via the clock-in Push flow without anyone
            // dragging it to a MarksTaskComplete status column - see GetMyActiveTasksAsync,
            // which excludes such tasks from "active" for the same reason.
            if (row.MarksTaskComplete || row.ProgressPercent >= 100)
                completed++;
            else if (row.DueDate is { } dueDate && dueDate < today)
                overdue++;
            else if (row.ProgressPercent > 0)
                inProgress++;
            else
                notStarted++;
        }

        return Result<TaskProgressResponse>.Success(
            new TaskProgressResponse(completed, inProgress, notStarted, overdue, rows.Count));
    }
}
