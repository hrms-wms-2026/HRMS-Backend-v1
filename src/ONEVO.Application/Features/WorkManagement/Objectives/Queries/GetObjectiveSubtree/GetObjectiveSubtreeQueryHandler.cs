using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;

public class GetObjectiveSubtreeQueryHandler : IRequestHandler<GetObjectiveSubtreeQuery, Result<ObjectiveSubtreeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;

    public GetObjectiveSubtreeQueryHandler(ICurrentUser currentUser, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _objectives = objectives;
    }

    public async Task<Result<ObjectiveSubtreeResponse>> Handle(GetObjectiveSubtreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<ObjectiveSubtreeResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Only this milestone's head can view its subtree.");

        var all = await _objectives.GetAllByProjectIdAsync(tenantId, objective.ProjectId, ct);

        var parent = objective.ParentObjectiveId is Guid parentId
            ? all.FirstOrDefault(o => o.Id == parentId)
            : null;

        var childrenByParent = all
            .Where(o => o.ParentObjectiveId.HasValue)
            .ToLookup(o => o.ParentObjectiveId!.Value);

        var response = new ObjectiveSubtreeResponse(
            parent is null ? null : ObjectiveMapper.ToDetail(parent),
            ObjectiveMapper.ToSubtreeNode(objective, childrenByParent));

        return Result<ObjectiveSubtreeResponse>.Success(response);
    }
}
