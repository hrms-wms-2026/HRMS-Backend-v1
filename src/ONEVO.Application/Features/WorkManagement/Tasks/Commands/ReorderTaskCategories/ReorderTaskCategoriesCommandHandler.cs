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

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskCategories;

public class ReorderTaskCategoriesCommandHandler : IRequestHandler<ReorderTaskCategoriesCommand, Result<IReadOnlyList<TaskCategoryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskCategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public ReorderTaskCategoriesCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskCategoryRepository categories, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _categories = categories;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result<IReadOnlyList<TaskCategoryResponse>>> Handle(ReorderTaskCategoriesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskCategoryResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskCategoryResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<TaskCategoryResponse>>.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<IReadOnlyList<TaskCategoryResponse>>.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<TaskCategoryResponse>>.Forbidden("Only an owner or member of this project can restructure the board.");

        // Defense in depth beyond the validator (which runs in the MediatR pipeline in production,
        // but not when a test calls Handle directly).
        if (request.Updates is null || request.Updates.Any(u => u is null))
            return Result<IReadOnlyList<TaskCategoryResponse>>.Failure("Updates must not contain null entries.", 422);

        if (request.Updates.Select(u => u.CategoryId).Distinct().Count() != request.Updates.Count)
            return Result<IReadOnlyList<TaskCategoryResponse>>.Failure("Updates must not contain duplicate category IDs.", 422);

        var existing = await _categories.GetByProjectIdAsync(tenantId, project.Id, ct);
        var byId = existing.ToDictionary(c => c.Id);

        foreach (var update in request.Updates)
        {
            if (!byId.TryGetValue(update.CategoryId, out var category))
                return Result<IReadOnlyList<TaskCategoryResponse>>.NotFound($"Category {update.CategoryId} not found on this project.");

            category.DisplayOrder = update.DisplayOrder;
            category.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var category in existing.Where(c => request.Updates.Any(u => u.CategoryId == c.Id)))
                _categories.Update(category);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<IReadOnlyList<TaskCategoryResponse>>.Success(
                existing.OrderBy(c => c.DisplayOrder)
                    .Select(c => new TaskCategoryResponse(c.Id, c.Name, c.DisplayOrder))
                    .ToList());
        }, ct);
    }
}
