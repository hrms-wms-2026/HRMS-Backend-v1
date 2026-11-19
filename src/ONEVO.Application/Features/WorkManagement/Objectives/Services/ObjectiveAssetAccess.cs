using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>The caller and Module an asset request runs against, once access is confirmed.</summary>
public sealed record ObjectiveAssetCaller(Guid TenantId, Guid UserId, Guid EmployeeId, Objective Objective, bool IsFullAdmin)
{
    /// <summary>Who may remove an attachment: whoever uploaded it, the Module's head, or a full admin.</summary>
    public bool CanRemove(Guid uploadedByUserId) =>
        IsFullAdmin || uploadedByUserId == UserId || Objective.OwnerId == EmployeeId;
}

/// <summary>
/// The access rule shared by the Module attachment endpoints (upload, delete, download): the same
/// rule as reading the Module itself in GetObjectiveByIdQueryHandler - a "*" permission, or active
/// membership of the Module or any ancestor (IModuleReadAccess).
/// </summary>
public sealed class ObjectiveAssetAccess
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IModuleReadAccess _readAccess;
    private readonly IPermissionResolver _permissionResolver;

    public ObjectiveAssetAccess(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IModuleReadAccess readAccess, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _readAccess = readAccess;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ObjectiveAssetCaller>> ResolveAsync(Guid objectiveId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveAssetCaller>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveAssetCaller>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (employeeId is null)
            return Result<ObjectiveAssetCaller>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, objectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveAssetCaller>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var isFullAdmin = permissions.Contains("*");
        if (!isFullAdmin && !await _readAccess.CanReadAsync(tenantId, objective, employeeId.Value, ct))
            return Result<ObjectiveAssetCaller>.Forbidden("You do not have access to this milestone.");

        return Result<ObjectiveAssetCaller>.Success(new ObjectiveAssetCaller(tenantId, userId, employeeId.Value, objective, isFullAdmin));
    }
}
