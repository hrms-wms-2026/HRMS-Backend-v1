using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskCategory;

public class EditTaskCategoryCommandHandler : IRequestHandler<EditTaskCategoryCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskCategoryRepository _categories;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public EditTaskCategoryCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskCategoryRepository categories,
        IObjectiveRepository objectives, IProjectRepository projects, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _categories = categories;
        _objectives = objectives;
        _projects = projects;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(EditTaskCategoryCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
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
            return Result.Forbidden("Only an owner or member of this project can edit task categories.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            category.Name = request.Name.Trim();
            category.DisplayOrder = request.DisplayOrder;
            category.UpdatedAt = DateTimeOffset.UtcNow;
            _categories.Update(category);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
