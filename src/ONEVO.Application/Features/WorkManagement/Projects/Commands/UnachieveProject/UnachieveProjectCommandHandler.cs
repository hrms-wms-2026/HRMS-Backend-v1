using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.UnachieveProject;

public class UnachieveProjectCommandHandler : IRequestHandler<UnachieveProjectCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;

    public UnachieveProjectCommandHandler(ICurrentUser currentUser, IProjectRepository projects, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnachieveProjectCommand request, CancellationToken ct)
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
            return Result.Forbidden("Only the project lead can un-achieve this project.");

        if (!project.IsAchieved)
            return Result.Conflict("Project is not achieved.");

        project.IsAchieved = false;
        project.AchievedAt = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        _projects.Update(project);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
