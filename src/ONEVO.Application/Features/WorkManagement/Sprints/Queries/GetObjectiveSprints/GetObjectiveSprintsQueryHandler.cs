using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetObjectiveSprints;

public class GetObjectiveSprintsQueryHandler : IRequestHandler<GetObjectiveSprintsQuery, Result<IReadOnlyList<SprintResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ISprintRepository _sprints;

    public GetObjectiveSprintsQueryHandler(ICurrentUser currentUser, ISprintRepository sprints)
    {
        _currentUser = currentUser;
        _sprints = sprints;
    }

    public async Task<Result<IReadOnlyList<SprintResponse>>> Handle(GetObjectiveSprintsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var sprints = request.ActiveOnly
            ? await _sprints.GetActiveByObjectiveIdAsync(tenantId, request.ObjectiveId, ct)
            : await _sprints.GetByObjectiveIdAsync(tenantId, request.ObjectiveId, ct);

        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => new SprintResponse(s.Id, s.ObjectiveId, s.Name, s.StartDate, s.EndDate, s.Status, s.CompletedAt, s.AchievedAt)).ToList());
    }
}
