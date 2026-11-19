using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;

/// <summary>
/// Newest-first feed of what an employee did, over existing tables (Option C in the spec: a future
/// activity-events table can replace the repository behind the same response). No module
/// permission: employees:read + coverage only.
/// </summary>
public sealed class GetEmployeeRecentActivityQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IEmployeeActivityFeedRepository feed,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeRecentActivityQuery, Result<EmployeeRecentActivityResponse>>
{
    public const int MaxLimit = 50;

    private static readonly Dictionary<string, string> Actions = new()
    {
        ["attendance_clock_in"] = "Clocked in",
        ["attendance_clock_out"] = "Clocked out",
        ["leave_requested"] = "Requested leave",
        ["attendance_correction_requested"] = "Requested an attendance correction",
        ["work_area_change_requested"] = "Requested a work area change",
        ["location_change_requested"] = "Requested a location change",
        ["task_created"] = "Created task",
        ["task_status_changed"] = "Changed task status",
        ["task_edited"] = "Edited task",
        ["task_progress_changed"] = "Updated task progress",
        ["task_commented"] = "Commented on task",
        ["task_clocked_in"] = "Started working on task",
        ["task_clocked_out"] = "Stopped working on task",
        ["module_joined"] = "Joined"
    };

    public async Task<Result<EmployeeRecentActivityResponse>> Handle(GetEmployeeRecentActivityQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeRecentActivityResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Limit < 1 || request.Limit > MaxLimit)
            return Result<EmployeeRecentActivityResponse>.Failure($"limit must be between 1 and {MaxLimit}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeRecentActivityResponse>.NotFound("The employee or selected organization record could not be found.");

        // One extra row tells us whether an older page exists.
        var rows = await feed.ListAsync(tenantId, request.EmployeeId, employee.UserId, request.Before, request.Limit + 1, ct);
        var page = rows.Take(request.Limit)
            .Select(r => new EmployeeActivityItem(
                $"{r.Kind}:{r.SourceId}", r.Kind, Actions.GetValueOrDefault(r.Kind, "Activity"), r.Target, r.Detail, r.At))
            .ToList();

        DateTimeOffset? nextBefore = rows.Count > request.Limit ? page[^1].At : null;
        return Result<EmployeeRecentActivityResponse>.Success(new EmployeeRecentActivityResponse(page, nextBefore, IsPartial: true));
    }
}
