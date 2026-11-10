using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.LinkProjectLogo;

/// <summary>Links a pending project_cover upload as the project's logo. A project has one picture, so this clears its icon. Lead only, like EditProject.</summary>
public sealed class LinkProjectLogoCommandHandler : IRequestHandler<LinkProjectLogoCommand, Result<ProjectLogoResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IPrimaryEntityAssetLinker _linker;
    private readonly IUnitOfWork _unitOfWork;

    public LinkProjectLogoCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IPrimaryEntityAssetLinker linker,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _linker = linker;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProjectLogoResponse>> Handle(LinkProjectLogoCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectLogoResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectLogoResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ProjectLogoResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetTrackedByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<ProjectLogoResponse>.NotFound("Project not found.");
        if (project.SystemPurpose == ProjectSystemPurposes.Office)
            return Result<ProjectLogoResponse>.Conflict("The system-managed Office project cannot be edited.");
        if (project.LeadId != callerEmployeeId.Value)
            return Result<ProjectLogoResponse>.Forbidden("Only the project lead can edit this project.");

        var linked = await _linker.LinkAsync(
            tenantId, userId, EntityAssetOwnerTypes.Project, project.Id, UploadPurposeCatalog.ProjectCover, request.FileId, ct);
        if (!linked.IsSuccess)
            return Result<ProjectLogoResponse>.Failure(linked.Error!, linked.StatusCode ?? 400);

        project.Icon = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProjectLogoResponse>.Success(new ProjectLogoResponse(project.Id, linked.Value));
    }
}
