using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskCategory;

public class DeleteTaskCategoryCommandHandler : IRequestHandler<DeleteTaskCategoryCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskCategoryRepository _categories;
    private readonly IWorkTaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public DeleteTaskCategoryCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskCategoryRepository categories, IWorkTaskRepository tasks,
        IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _categories = categories;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(DeleteTaskCategoryCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var category = await _categories.GetByIdForTenantAsync(tenantId, request.CategoryId, ct);
        if (category is null)
            return Result.NotFound("Task category not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, category.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only an owner or member of this project can delete task categories.");

        if (await _tasks.AnyActiveByCategoryIdAsync(tenantId, category.Id, ct))
            return Result.Conflict("Move all tasks out of this category before deleting it.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _categories.Remove(category);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
