using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.RemoveProjectLogo;

/// <summary>Unlinks and deletes the project's logo. Lead only, like EditProject.</summary>
public sealed class RemoveProjectLogoCommandHandler : IRequestHandler<RemoveProjectLogoCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IPrimaryEntityAssetLinker _linker;

    public RemoveProjectLogoCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IPrimaryEntityAssetLinker linker)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _linker = linker;
    }

    public async Task<Result> Handle(RemoveProjectLogoCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");
        if (project.SystemPurpose == ProjectSystemPurposes.Office)
            return Result.Conflict("The system-managed Office project cannot be edited.");
        if (project.LeadId != callerEmployeeId.Value)
            return Result.Forbidden("Only the project lead can edit this project.");

        return await _linker.RemoveAsync(
            tenantId, userId, EntityAssetOwnerTypes.Project, project.Id, UploadPurposeCatalog.ProjectCover, ct);
    }
}
