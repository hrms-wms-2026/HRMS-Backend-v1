using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkTasks;

/// <summary>
/// The task list behind the Overview Work card: the same period rows and bucket rules as
/// GetEmployeeWorkOverviewQueryHandler, so the list always adds up to the card's counts.
/// Loaded only when the card is opened.
/// </summary>
public sealed class GetEmployeeWorkTasksQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeWorkTasksQuery, Result<EmployeeWorkTasksResponse>>
{
    private static readonly string[] BucketOrder =
    [
        EmployeeTaskBuckets.Overdue, EmployeeTaskBuckets.NotStarted,
        EmployeeTaskBuckets.InProgress, EmployeeTaskBuckets.Completed
    ];

    private static readonly string[] PriorityOrder =
    [
        WorkTaskPriorities.Critical, WorkTaskPriorities.High, WorkTaskPriorities.Medium, WorkTaskPriorities.Low
    ];

    public async Task<Result<EmployeeWorkTasksResponse>> Handle(GetEmployeeWorkTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkTasksResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeWorkTasksResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var rows = await tasks.ListForEmployeePeriodAsync(tenantId, request.EmployeeId, period.Value!.From, period.Value.To, ct);
        var asOf = period.Value.To < clock.Today ? period.Value.To : clock.Today;

        var items = rows
            .Select(row => new EmployeeWorkTaskItem(
                row.TaskId, row.ShortId, row.Title,
                EmployeeTaskPeriodCalculator.Bucket(row, asOf),
                string.IsNullOrEmpty(row.Priority) ? WorkTaskPriorities.Medium : row.Priority,
                row.StatusName, row.StatusColor, row.DueDate,
                EmployeeTaskPeriodCalculator.DaysOverdue(row, asOf), row.IsCarriedOver,
                row.ProjectId, row.ProjectName, row.ObjectiveId, row.ObjectiveTitle))
            .OrderBy(i => Rank(BucketOrder, i.Bucket))
            .ThenBy(i => Rank(PriorityOrder, i.Priority))
            .ThenBy(i => i.DueDate is null)
            .ThenBy(i => i.DueDate)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<EmployeeWorkTasksResponse>.Success(new EmployeeWorkTasksResponse(period.Value.From, period.Value.To, items));
    }

    /// <summary>Position in the given order; unknown values sort last.</summary>
    private static int Rank(string[] order, string value)
    {
        var index = Array.IndexOf(order, value);
        return index < 0 ? order.Length : index;
    }
}
