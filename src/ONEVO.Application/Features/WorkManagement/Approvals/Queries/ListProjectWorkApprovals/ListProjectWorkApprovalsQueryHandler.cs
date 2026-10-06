using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;

public sealed class ListProjectWorkApprovalsQueryHandler
    : IRequestHandler<ListProjectWorkApprovalsQuery, Result<IReadOnlyList<WorkApprovalRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;

    public ListProjectWorkApprovalsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
    }

    public async Task<Result<IReadOnlyList<WorkApprovalRequestResponse>>> Handle(ListProjectWorkApprovalsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var tree = await _hierarchy.LoadTreeAsync(tenantId, query.ProjectId, ct);
        IReadOnlyList<WorkApprovalRequest> rows;
        switch (query.Scope)
        {
            case "inbox":
                var pending = await _requests.ListByProjectAsync(tenantId, query.ProjectId, null, WorkApprovalRequestStatuses.Pending, ct);
                rows = pending.Where(r => WorkApprovalDecisionRules.CanDecide(tree, r, caller)).ToList();
                break;
            case "mine":
                rows = await _requests.ListByProjectAsync(tenantId, query.ProjectId, caller, query.Status, ct);
                break;
            default:
                return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Failure("Scope must be 'inbox' or 'mine'.");
        }

        var ids = rows.SelectMany(r => new[] { r.RequestedByEmployeeId, r.ApproverEmployeeId }).Distinct().ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, ids, ct);
        return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Success(
            rows.Select(r => WorkApprovalRequestMapper.ToResponse(r, names, caller, tree)).ToList());
    }
}
