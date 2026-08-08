using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;

public class GetMyObjectiveHistoryQueryHandler : IRequestHandler<GetMyObjectiveHistoryQuery, Result<IReadOnlyList<ObjectiveHistoryItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetMyObjectiveHistoryQueryHandler(ICurrentUser currentUser, IProjectMemberRepository members, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _members = members;
        _objectives = objectives;
    }

    public async Task<Result<IReadOnlyList<ObjectiveHistoryItemResponse>>> Handle(GetMyObjectiveHistoryQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveHistoryItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveHistoryItemResponse>>.Forbidden("Tenant context missing.");

        var inactiveMemberships = await _members.ListInactiveMembershipsForUserAsync(tenantId, userId, ct);

        var items = new List<ObjectiveHistoryItemResponse>();
        foreach (var membership in inactiveMemberships)
        {
            var objective = await _objectives.GetByIdForTenantAsync(tenantId, membership.ObjectiveId, ct);
            if (objective is null)
                continue;

            items.Add(new ObjectiveHistoryItemResponse(objective.Id, objective.Title, objective.ProjectId, objective.IsAchieved, membership.RemovedAt));
        }

        return Result<IReadOnlyList<ObjectiveHistoryItemResponse>>.Success(items);
    }
}
