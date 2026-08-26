using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskHistory;

public sealed class GetTaskHistoryQueryHandler : IRequestHandler<GetTaskHistoryQuery, Result<TaskHistoryResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskEditLogRepository _editLogs;
    private readonly ITaskStatusChangeLogRepository _statusChangeLogs;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ITaskPercentageLogRepository _percentageLogs;

    public GetTaskHistoryQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskEditLogRepository editLogs, ITaskStatusChangeLogRepository statusChangeLogs,
        ITaskClockingSessionRepository sessions, ITaskPercentageLogRepository percentageLogs)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _editLogs = editLogs;
        _statusChangeLogs = statusChangeLogs;
        _sessions = sessions;
        _percentageLogs = percentageLogs;
    }

    public async Task<Result<TaskHistoryResponse>> Handle(GetTaskHistoryQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskHistoryResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<TaskHistoryResponse>.NotFound("Task not found.");

        var editLogs = await _editLogs.GetForTaskAsync(tenantId, task.Id, ct);
        var statusChangeLogs = await _statusChangeLogs.GetForTaskAsync(tenantId, task.Id, ct);
        var sessions = await _sessions.GetForTaskAsync(tenantId, task.Id, ct);
        var percentageLogs = await _percentageLogs.GetForTaskAsync(tenantId, task.Id, ct);

        var percentageLogsBySessionId = percentageLogs
            .Where(log => log.ClockingSessionId.HasValue)
            .ToDictionary(log => log.ClockingSessionId!.Value);
        var standalonePercentageLogs = percentageLogs.Where(log => !log.ClockingSessionId.HasValue).ToList();

        var entries = new List<TaskHistoryEntryResponse>();

        foreach (var log in editLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.Edit, log.ChangedAt, log.EmployeeId, string.Empty,
                new TaskEditEntryDetails(log.Id, log.Source, log.EditRequestId, log.OldValuesJson, log.NewValuesJson, log.Reason),
                null, null, null));
        }

        foreach (var log in statusChangeLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.StatusChange, log.ChangedAt, log.EmployeeId, string.Empty,
                null, new TaskStatusChangeEntryDetails(log.FromStatusId, log.ToStatusId), null, null));
        }

        foreach (var session in sessions)
        {
            percentageLogsBySessionId.TryGetValue(session.Id, out var matchedPush);
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.ClockSession, session.ClockOutAt ?? session.ClockInAt, session.EmployeeId, string.Empty,
                null, null,
                new TaskClockSessionEntryDetails(
                    session.Id, session.ClockInAt, session.ClockOutAt, session.DurationMinutes, session.Reason,
                    matchedPush?.NewPercent, matchedPush?.PreviousPercent, matchedPush?.Id, matchedPush?.Reason),
                null));
        }

        foreach (var log in standalonePercentageLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.PercentageChange, log.ChangedAt, log.EmployeeId, string.Empty,
                null, null, null,
                new TaskPercentageChangeEntryDetails(log.Id, log.PreviousPercent, log.NewPercent, log.Source, log.Reason)));
        }

        var employeeIds = entries.Select(entry => entry.EmployeeId).Distinct().ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct);
        entries = entries
            .Select(entry => entry with { EmployeeName = names.GetValueOrDefault(entry.EmployeeId) ?? "A teammate" })
            .OrderByDescending(entry => entry.OccurredAt)
            .ToList();

        return Result<TaskHistoryResponse>.Success(new TaskHistoryResponse(entries));
    }
}
