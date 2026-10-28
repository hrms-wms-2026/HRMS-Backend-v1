using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>Where a meeting goes and whether the caller may put it there now (<see cref="Direct"/>) or must ask.</summary>
public sealed record MeetingPlacement(Guid ProjectId, Guid ObjectiveId, bool IsOffice, bool Direct);

public interface IMeetingPlacementResolver
{
    /// <summary>D4. A project meeting (<paramref name="projectId"/> set): the module's owner or an owner of an ancestor
    /// module creates directly; another member of the module requests approval; anyone else gets 403. An Office meeting
    /// (no project): calendar:write, placed at the Office project's root and always direct.</summary>
    Task<Result<MeetingPlacement>> ResolveAsync(Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct);
}

public sealed class MeetingPlacementResolver : IMeetingPlacementResolver
{
    public const string CalendarWritePermission = "calendar:write";

    private readonly IWorkHierarchyService _hierarchy;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly ICurrentUser _currentUser;
    private readonly IOfficeProjectReconciler _officeReconciler;

    public MeetingPlacementResolver(IWorkHierarchyService hierarchy, IMilestoneMembershipCoordinator membership,
        IProjectRepository projects, IObjectiveRepository objectives, ICurrentUser currentUser, IOfficeProjectReconciler officeReconciler)
    {
        _hierarchy = hierarchy;
        _membership = membership;
        _projects = projects;
        _objectives = objectives;
        _currentUser = currentUser;
        _officeReconciler = officeReconciler;
    }

    public async Task<Result<MeetingPlacement>> ResolveAsync(
        Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct)
    {
        if (projectId is not Guid project)
            return await ResolveOfficeAsync(tenantId, ct);

        var tree = await _hierarchy.LoadTreeAsync(tenantId, project, ct);
        var module = objectiveId is Guid id ? tree.Get(id) : null;
        if (module is null || !module.IsActive || module.IsAchieved)
            return Result<MeetingPlacement>.Failure("This module is not available for meetings.", 400, MeetingErrorCodes.ModuleUnavailable);

        if (tree.IsAtOrAbove(callerEmployeeId, module.Id))
            return Result<MeetingPlacement>.Success(new MeetingPlacement(project, module.Id, IsOffice: false, Direct: true));
        // The same membership rule as creating a task in the module.
        if (await _membership.IsEffectiveManagerAsync(tenantId, module.Id, callerEmployeeId, ct))
            return Result<MeetingPlacement>.Success(new MeetingPlacement(project, module.Id, IsOffice: false, Direct: false));

        return Result<MeetingPlacement>.Failure(
            "Only members of this module can schedule meetings in it.", 403, MeetingErrorCodes.ModuleMemberRequired);
    }

    private async Task<Result<MeetingPlacement>> ResolveOfficeAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(CalendarWritePermission))
            return Result<MeetingPlacement>.Failure(
                "You need calendar access to schedule an Office meeting.", 403, MeetingErrorCodes.CalendarWriteRequired);

        var office = await _projects.GetOfficeProjectAsync(tenantId, ct);
        var root = office is null ? null : await _objectives.GetDefaultByProjectIdAsync(tenantId, office.Id, ct);
        if (office is null || root is null)
        {
            // The Office project is created on demand; the client retries once it exists.
            await _officeReconciler.ReconcileAsync(tenantId, ct);
            return Result<MeetingPlacement>.Failure(
                "Office meetings are being set up for your company. Try again in a moment.", 409, MeetingErrorCodes.OfficeNotReady);
        }

        return Result<MeetingPlacement>.Success(new MeetingPlacement(office.Id, root.Id, IsOffice: true, Direct: true));
    }
}
