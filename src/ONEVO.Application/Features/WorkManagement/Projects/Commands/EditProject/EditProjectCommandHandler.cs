using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;

public class EditProjectCommandHandler : IRequestHandler<EditProjectCommand, Result<ProjectDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectCategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;

    public EditProjectCommandHandler(
        ICurrentUser currentUser,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IProjectCategoryRepository categories,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _objectives = objectives;
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProjectDetailResponse>> Handle(EditProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectDetailResponse>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<ProjectDetailResponse>.NotFound("Project not found.");

        // Project is the tree's root node (milestone-hierarchy design §4) - only its own
        // Head, the Lead, has unrestricted control over the node itself. Same rule
        // DeleteProjectCommandHandler already enforces; no approval needed either, since
        // the root has no Reporting Manager to route a request to.
        if (project.LeadId != userId)
            return Result<ProjectDetailResponse>.Forbidden("Only the project lead can edit this project.");

        // Blank (empty/whitespace-only) is treated the same as omitted, not as "change to
        // blank" - many JSON clients default an untouched optional string field to "" rather
        // than null, and that must not trip a spurious 400 on every ordinary edit.
        if (!string.IsNullOrWhiteSpace(request.Identifier))
        {
            var normalizedIdentifier = request.Identifier.Trim().ToUpperInvariant();
            if (!string.Equals(normalizedIdentifier, project.Identifier, StringComparison.Ordinal))
                return Result<ProjectDetailResponse>.Failure("Project identifier cannot be changed after creation.");
        }

        var category = await _categories.GetByIdForTenantAsync(tenantId, request.CategoryId, ct);
        if (category is null || !category.IsActive)
            return Result<ProjectDetailResponse>.NotFound("Project category not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<ProjectDetailResponse>.NotFound("Default objective not found for this project.");

        var now = DateTimeOffset.UtcNow;

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.CategoryId = category.Id;
        project.StartDate = request.StartDate;
        project.TargetDate = request.TargetDate;
        project.Color = request.Color;
        project.ActualHours = request.ActualHours;
        project.UpdatedAt = now;

        // Default Objective mirrors the Project's title/description/dates and "stays in sync
        // on Project edit" per phase1-table-inventory.md. LeadId/Identifier/AllocatedHours/
        // CompletedHours/IsActive are intentionally left untouched - not part of this request.
        defaultObjective.Title = project.Name;
        defaultObjective.Description = project.Description;
        defaultObjective.StartDate = project.StartDate;
        defaultObjective.EndDate = project.TargetDate;
        defaultObjective.UpdatedAt = now;

        _projects.Update(project);
        _objectives.Update(defaultObjective);
        await _unitOfWork.SaveChangesAsync(ct);

        // Always true here - the lead-only check above already rejected any non-lead caller.
        return Result<ProjectDetailResponse>.Success(ProjectMapper.ToDetail(project, isLead: true));
    }
}
