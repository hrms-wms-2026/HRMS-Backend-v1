using System.Globalization;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Queries.CheckTaskLoad;

public sealed class CheckTaskLoadQueryHandler : IRequestHandler<CheckTaskLoadQuery, Result<TaskLoadCheckResponse>>
{
    private static readonly TaskLoadCheckResponse NoWarnings = new([]);

    private readonly IProjectMonitorCallerResolver _callers;
    private readonly IProjectMonitorSnapshotLoader _snapshots;
    private readonly ICallerIdentityResolver _identity;
    private readonly IDateTimeProvider _clock;

    public CheckTaskLoadQueryHandler(
        IProjectMonitorCallerResolver callers, IProjectMonitorSnapshotLoader snapshots,
        ICallerIdentityResolver identity, IDateTimeProvider clock)
    {
        _callers = callers;
        _snapshots = snapshots;
        _identity = identity;
        _clock = clock;
    }

    public async Task<Result<TaskLoadCheckResponse>> Handle(CheckTaskLoadQuery query, CancellationToken ct)
    {
        var caller = await _callers.ResolveAsync(query.ProjectId, ct);
        if (!caller.IsSuccess)
            return Result<TaskLoadCheckResponse>.Failure(caller.Error!, caller.StatusCode ?? 403);

        var today = _clock.Today;
        var assignees = query.AssigneeEmployeeIds.Distinct().ToList();
        if (assignees.Count == 0 || query.DueDate is not { } due || due < today || query.EstimatedHours is not > 0)
            return Result<TaskLoadCheckResponse>.Success(NoWarnings);

        var tenantId = caller.Value!.TenantId;
        var snapshot = await _snapshots.LoadAsync(tenantId, query.ProjectId, ct);

        // Swap the edited task (or add the new one) with the values on the form.
        var existing = query.TaskId is { } taskId ? snapshot.Tasks.FirstOrDefault(t => t.Id == taskId) : null;
        var candidate = new MonitorTask(
            existing?.Id ?? Guid.NewGuid(), existing?.ModuleId ?? Guid.Empty, null, existing?.SprintId, existing?.Title ?? "New task",
            due, query.EstimatedHours, existing?.CompletedHours ?? 0m, IsComplete: false, existing?.ClockedHours ?? 0m, assignees);
        var tasks = snapshot.Tasks.Where(t => t.Id != candidate.Id).Append(candidate).ToList();

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, assignees, ct);
        var warnings = new List<MonitorWarning>();
        foreach (var employeeId in assignees)
        {
            var finding = ProjectMonitorRules.EmployeeDeadlineOverload(employeeId, tasks, snapshot.Calendar, today)
                .FirstOrDefault(f => f.TargetId == candidate.Id);
            if (finding is null)
                continue;

            var name = names.GetValueOrDefault(employeeId) ?? "An assignee";
            var demand = (decimal)finding.Details["demandHours"]!;
            var capacity = (decimal)finding.Details["capacityHours"]!;
            var deadline = DateOnly.ParseExact((string)finding.Details["dueDate"]!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            warnings.Add(new MonitorWarning(finding.RuleCode,
                $"{name} has {ProjectMonitorRules.Hours(demand)} h of work due by {deadline.ToString("MMM d", CultureInfo.InvariantCulture)} but only {ProjectMonitorRules.Hours(capacity)} h of working time.",
                employeeId));
        }

        return Result<TaskLoadCheckResponse>.Success(new TaskLoadCheckResponse(warnings));
    }
}
