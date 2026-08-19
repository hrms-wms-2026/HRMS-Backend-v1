using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;

public class GetObjectiveTaskStatusesQueryHandler : IRequestHandler<GetObjectiveTaskStatusesQuery, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public GetObjectiveTaskStatusesQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(GetObjectiveTaskStatusesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Objective not found.");

        var existing = await _statuses.GetByObjectiveIdAsync(tenantId, request.ObjectiveId, ct);
        if (existing.Count > 0)
            return Result<IReadOnlyList<TaskStatusResponse>>.Success(ToResponses(existing));

        var template = await _statuses.GetProjectTemplateAsync(tenantId, objective.ProjectId, ct);
        var now = DateTimeOffset.UtcNow;
        var copies = template.Select(t => new TaskStatusEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = request.ObjectiveId,
            Name = t.Name, DisplayOrder = t.DisplayOrder, RequiresApproval = t.RequiresApproval,
            MarksTaskComplete = t.MarksTaskComplete, Visibility = t.Visibility,
            CreatedById = _currentUser.UserId, CreatedAt = now
        }).ToList();

        if (copies.Count == 0)
            return Result<IReadOnlyList<TaskStatusResponse>>.Success(Array.Empty<TaskStatusResponse>());

        await _statuses.AddRangeAsync(copies, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<IReadOnlyList<TaskStatusResponse>>.Success(ToResponses(copies));
    }

    private static IReadOnlyList<TaskStatusResponse> ToResponses(IReadOnlyList<TaskStatusEntity> statuses)
        => statuses.OrderBy(s => s.DisplayOrder)
            .Select(s => new TaskStatusResponse(
                s.Id, s.Name, s.DisplayOrder, s.RequiresApproval,
                s.ApproverId, s.MarksTaskComplete, s.Visibility))
            .ToList();
}
