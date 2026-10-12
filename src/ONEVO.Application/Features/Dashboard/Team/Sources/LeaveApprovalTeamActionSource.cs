using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: leave requests pending the caller's own approval
/// (My Team spec §8.2). Reuses ILeaveApprovalRepository.ListPendingForApproverAsync verbatim -
/// the exact method ListPendingLeaveApprovalsQueryHandler already calls for the pending-approvals
/// screen - including its post-query per-row LeaveApprovalModeEvaluator.IsActionable filter
/// (multi-approver sequence/mode logic that a raw repository predicate cannot express), so the
/// count/top-N here can never drift from what that screen shows.</summary>
public sealed class LeaveApprovalTeamActionSource(
    ICurrentUser currentUser, IEmployeeRepository employees, ILeaveApprovalRepository repository)
    : ITeamActionSource
{
    private const string Permission = "leave:approve";

    public string Key => "leave.approval";
    public string Domain => ActionSourceSummary.DomainPeople;

    public Task<bool> IsGatedAsync(CancellationToken ct = default)
        => Task.FromResult(currentUser.HasPermission(Permission));

    public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
    {
        var employee = await employees.GetByUserIdAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, null, null, []);

        var rows = await repository.ListPendingForApproverAsync(
            currentUser.TenantId, employee.Id,
            new LeaveApprovalListFilter(null, null, null, null, null, legalEntityId), ct);

        var actionable = new List<(LeavePendingApprovalListRow Row, DateTimeOffset CreatedAt)>();
        foreach (var row in rows)
        {
            var state = await repository.GetStateAsync(currentUser.TenantId, row.Request.Id, ct);
            if (state?.ApprovalMode is null)
                continue;

            var modeRows = state.Approvers
                .Select(a => new ApprovalModeRow(a.ApproverEmployeeId, a.SequenceOrder, a.Status))
                .ToList();
            if (!LeaveApprovalModeEvaluator.IsActionable(state.ApprovalMode, modeRows, employee.Id))
                continue;

            actionable.Add((row, row.Request.CreatedAt));
        }

        var ordered = actionable.OrderBy(x => x.CreatedAt).ToList();
        var oldest = ordered.Count > 0 ? ordered[0].CreatedAt : (DateTimeOffset?)null;

        var topItems = ordered.Take(top).Select(x => new ActionItem(
            Key,
            x.Row.Request.Id,
            $"Leave request - {x.Row.EmployeeName}",
            x.Row.Request.EmployeeId,
            x.Row.EmployeeName,
            x.CreatedAt,
            null,
            new ActionItemLink(ActionItemLink.KindLeaveApproval, new Dictionary<string, string>
            {
                ["requestId"] = x.Row.Request.Id.ToString(),
            }))).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, null, oldest, topItems);
    }
}
