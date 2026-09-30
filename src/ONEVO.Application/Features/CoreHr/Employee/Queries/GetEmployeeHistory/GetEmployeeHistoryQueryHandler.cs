using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;

/// <summary>
/// A lifetime timeline built only from facts that leave a record: hire date, probation end,
/// primary-employment position assignments (position and reporting-manager changes), approved
/// leave, completed checklist tasks and termination. Employment status/type/work-mode changes are
/// not recorded anywhere, so they cannot appear.
/// </summary>
public sealed class GetEmployeeHistoryQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IPositionAssignmentRepository assignments,
    IPositionRepository positions,
    ILeaveRequestRepository leave,
    IEmployeeChecklistTaskRepository checklist,
    ICallerIdentityResolver identity,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeHistoryQuery, Result<EmployeeHistoryResponse>>
{
    public const int MaxLimit = 50;

    public async Task<Result<EmployeeHistoryResponse>> Handle(GetEmployeeHistoryQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeHistoryResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Limit < 1 || request.Limit > MaxLimit)
            return Result<EmployeeHistoryResponse>.Failure($"limit must be between 1 and {MaxLimit}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeHistoryResponse>.NotFound("The employee or selected organization record could not be found.");

        var today = clock.Today;
        var history = await assignments.ListHistoryForEmployeeAsync(tenantId, request.EmployeeId, ct);
        var positionNames = (await positions.GetByIdsAsync(tenantId, history.Select(h => h.PositionId).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id, p => p.Name);
        var managerIds = history.Where(h => h.ReportsToEmployeeId is not null).Select(h => h.ReportsToEmployeeId!.Value).Distinct().ToList();
        var managerNames = managerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, managerIds, ct)).ToDictionary(p => p.Key, p => p.Value);

        string PositionName(Guid id) => positionNames.GetValueOrDefault(id) ?? "Unknown position";

        var events = new List<EmployeeHistoryEvent>();

        events.Add(new EmployeeHistoryEvent(
            "joined", "Joined company",
            history.Count > 0 ? $"Joined as {PositionName(history[0].PositionId)}" : null,
            employee.HireDate));

        if (employee.ProbationEndDate is { } probationEnd && probationEnd <= today)
            events.Add(new EmployeeHistoryEvent("probation_completed", "Probation completed", null, probationEnd));

        for (var i = 1; i < history.Count; i++)
        {
            var previous = history[i - 1];
            var current = history[i];

            var reason = string.IsNullOrWhiteSpace(current.ChangeReason) ? "" : $" ({current.ChangeReason})";
            events.Add(new EmployeeHistoryEvent(
                "position_changed", "Position changed",
                $"{PositionName(previous.PositionId)} → {PositionName(current.PositionId)}{reason}",
                current.EffectiveFrom));

            if (current.ReportsToEmployeeId is { } managerId && managerId != previous.ReportsToEmployeeId)
            {
                events.Add(new EmployeeHistoryEvent(
                    "manager_changed", "Reporting manager changed",
                    $"Reporting manager changed to {managerNames.GetValueOrDefault(managerId) ?? "a new manager"}",
                    current.EffectiveFrom));
            }
        }

        var leaveRows = await leave.ListOwnAsync(tenantId, request.EmployeeId, new LeaveRequestListFilter(null, null, null, null), ct);
        events.AddRange(leaveRows
            .Where(r => r.Request.Status == "approved" && r.Request.ApprovedAt is not null)
            .Select(r => new EmployeeHistoryEvent(
                "leave_approved", "Leave approved",
                $"{r.LeaveTypeName} ({Math.Round(r.Request.TotalHours, 2)} h)",
                DateOnly.FromDateTime(r.Request.ApprovedAt!.Value.UtcDateTime))));

        var checklistRows = await checklist.ListByEmployeeAsync(tenantId, request.EmployeeId, ct);
        events.AddRange(checklistRows
            .Where(t => t.Status == EmployeeChecklistTaskStatuses.Completed && t.CompletedAt is not null)
            .Select(t => new EmployeeHistoryEvent(
                "checklist_completed", "Checklist task completed",
                $"{(string.IsNullOrWhiteSpace(t.Category) ? t.LifecycleType : t.Category.Trim())}: {t.TaskTitle}",
                DateOnly.FromDateTime(t.CompletedAt!.Value.UtcDateTime))));

        if (employee.TerminationDate is { } terminated && terminated <= today)
            events.Add(new EmployeeHistoryEvent("terminated", "Employment ended", null, terminated));

        var newestFirst = events
            .Select((e, index) => (e, index))
            .OrderByDescending(x => x.e.Date)
            .ThenBy(x => x.index)
            .Select(x => x.e)
            .Take(request.Limit)
            .ToList();

        return Result<EmployeeHistoryResponse>.Success(new EmployeeHistoryResponse(newestFirst));
    }
}
