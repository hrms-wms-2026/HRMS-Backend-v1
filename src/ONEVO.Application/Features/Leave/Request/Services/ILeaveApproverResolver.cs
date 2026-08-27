using Microsoft.Extensions.Options;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Request.Services;

public interface ILeaveApproverResolver
{
    Task<LeaveApproverResolution> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}

public sealed record LeaveApproverResolution(IReadOnlyList<LeaveApproverResolutionRow> Approvers);

public sealed record LeaveApproverResolutionRow(
    Guid ApproverEmployeeId,
    int SequenceOrder,
    Guid? DelegatedFromApproverId);

public sealed class LeaveApproverResolver : ILeaveApproverResolver
{
    private readonly IEmployeeHierarchyClosureRepository _hierarchy;
    private readonly ILeaveRequestRepository _requests;

    public LeaveApproverResolver(
        IEmployeeHierarchyClosureRepository hierarchy,
        ILeaveRequestRepository requests,
        IOptions<LeaveRequestOptions> options)
    {
        _hierarchy = hierarchy;
        _requests = requests;
        _ = options;
    }

    public async Task<LeaveApproverResolution> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var approverId = await _hierarchy.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, ct);
        if (approverId is null || approverId == employeeId)
            return new LeaveApproverResolution([]);

        var delegateRows = await _requests.ListActiveDelegatesAsync(
            tenantId, [approverId.Value], startDate, endDate, ct);
        var delegateRow = delegateRows.FirstOrDefault(row => row.ApproverEmployeeId == approverId.Value);
        if (delegateRow is not null && delegateRow.DelegateEmployeeId != employeeId)
        {
            return new LeaveApproverResolution([
                new LeaveApproverResolutionRow(delegateRow.DelegateEmployeeId, 1, delegateRow.ApproverEmployeeId)
            ]);
        }

        return new LeaveApproverResolution([
            new LeaveApproverResolutionRow(approverId.Value, 1, null)
        ]);
    }
}
