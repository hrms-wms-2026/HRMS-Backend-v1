using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
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

/// <summary>
/// Routes a leave request through the shared IEmployeeAuthorityResolver (position coverage, then
/// department coverage, then the reporting line - each candidate must hold leave:approve in the
/// employee's legal entity). When that finds nobody (e.g. the top of the org chart), the request
/// routes to one HR manager instead. An active delegation on the chosen approver applies either way.
/// </summary>
public sealed class LeaveApproverResolver : ILeaveApproverResolver
{
    public const string HrFallbackPermission = "leave:manage";
    public const string ApprovePermission = "leave:approve";

    private readonly IEmployeeAuthorityResolver _authority;
    private readonly ILeaveRequestRepository _requests;
    private readonly IPermissionRepository _permissions;
    private readonly IEmployeeRepository _employees;
    private readonly IDateTimeProvider _clock;

    public LeaveApproverResolver(
        IEmployeeAuthorityResolver authority,
        ILeaveRequestRepository requests,
        IPermissionRepository permissions,
        IEmployeeRepository employees,
        IDateTimeProvider clock,
        IOptions<LeaveRequestOptions> options)
    {
        _authority = authority;
        _requests = requests;
        _permissions = permissions;
        _employees = employees;
        _clock = clock;
        _ = options;
    }

    public async Task<LeaveApproverResolution> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var approverId = await ResolveAuthorityApproverAsync(tenantId, employeeId, ct)
            ?? await ResolveHrApproverAsync(tenantId, employeeId, ct);
        if (approverId is null)
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

    /// <summary>The shared authority route's approver, or null when it finds none. The resolver
    /// takes tenant context from ICurrentUser, which is the submitting user in this tenant.</summary>
    private async Task<Guid?> ResolveAuthorityApproverAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee?.LegalEntityId is not { } legalEntityId)
            return null;

        var route = await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            employeeId, legalEntityId, ApprovePermission, EmployeeAuthorityPurpose.TimeOffApproval), ct);
        return route.IsSuccess && route.Value is not null ? route.Value.ApproverEmployeeId : null;
    }

    /// <summary>
    /// One HR manager (holds both leave:manage and leave:approve - the approve endpoints are gated
    /// on the latter), never the requester, lowest employee number first. A single approver because
    /// the policy's approval mode is read live: several fallback rows under all_must_approve or
    /// in_order would make every HR user approve.
    /// </summary>
    private async Task<Guid?> ResolveHrApproverAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var managers = await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, HrFallbackPermission, now, ct);
        if (managers.Count == 0)
            return null;

        var approvers = await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, ApprovePermission, now, ct);
        var userIds = managers.Intersect(approvers).ToList();
        if (userIds.Count == 0)
            return null;

        var today = _clock.Today;
        var employees = await _employees.GetByUserIdsAsync(tenantId, userIds, ct);
        return employees
            .Where(e => e.Id != employeeId && (e.TerminationDate is null || e.TerminationDate > today))
            .OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal)
            .ThenBy(e => e.Id)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefault();
    }
}
