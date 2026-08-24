using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public class CreateTaskStatusCommandHandler : IRequestHandler<CreateTaskStatusCommand, Result<TaskStatusResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public CreateTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskStatusRepository statuses, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result<TaskStatusResponse>> Handle(CreateTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskStatusResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskStatusResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<TaskStatusResponse>.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<TaskStatusResponse>.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result<TaskStatusResponse>.Forbidden("Only an owner or member of this project can create task statuses.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var status = new TaskStatusEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, ObjectiveId = null,
                Name = request.Name.Trim(), DisplayOrder = request.DisplayOrder, Visibility = request.Visibility,
                MarksTaskComplete = request.MarksTaskComplete, RequiresApproval = request.RequiresApproval,
                ApproverId = request.ApproverId, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _statuses.AddAsync(status, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskStatusResponse>.Success(new TaskStatusResponse(
                status.Id, status.Name, status.DisplayOrder, status.RequiresApproval, status.ApproverId,
                status.MarksTaskComplete, status.Visibility));
        }, ct);
    }
}
