using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetProjectApprovalFeed;

public sealed class GetProjectApprovalFeedQueryHandler
    : IRequestHandler<GetProjectApprovalFeedQuery, Result<IReadOnlyList<ApprovalFeedItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly ApprovalFeedBuilder _feed;

    public GetProjectApprovalFeedQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects, ApprovalFeedBuilder feed)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _feed = feed;
    }

    public async Task<Result<IReadOnlyList<ApprovalFeedItemResponse>>> Handle(GetProjectApprovalFeedQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ApprovalFeedItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ApprovalFeedItemResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, query.ProjectId, ct);
        if (project is null)
            return Result<IReadOnlyList<ApprovalFeedItemResponse>>.NotFound("Project not found.");

        var entries = await _feed.BuildAsync(tenantId, project, callerEmployeeId.Value, ct);
        return Result<IReadOnlyList<ApprovalFeedItemResponse>>.Success(ApprovalFeedItemFactory.Sort(entries.Select(e => e.Item)));
    }
}
