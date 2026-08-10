using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.AchieveProject;

public class AchieveProjectCommandHandler : IRequestHandler<AchieveProjectCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveProjectCommandHandler(
        ICurrentUser currentUser, IProjectRepository projects, IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AchieveProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result.NotFound("Project not found.");

        if (project.LeadId != userId)
            return Result.Forbidden("Only the project lead can achieve this project.");

        if (project.IsAchieved)
            return Result.Conflict("Project is already achieved.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result.NotFound("Default objective not found for this project.");

        var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, defaultObjective.Id, ct);
        if (directChildren.Any(c => !c.IsAchieved))
            return Result.Failure("All top-level milestones must be achieved before the project can be.");

        project.IsAchieved = true;
        project.AchievedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        _projects.Update(project);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
