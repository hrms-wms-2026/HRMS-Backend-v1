using System.Text.Json;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public sealed class SprintActionSubmitter : ISprintActionSubmitter
{
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberRepository _members;
    private readonly ICallerIdentityResolver _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IWorkNotificationEngine _notifications;
    private readonly ISprintRepository _sprints;

    public SprintActionSubmitter(
        IWorkHierarchyService hierarchy, IProjectMemberRepository members, ICallerIdentityResolver identity,
        IUnitOfWork unitOfWork, IWorkApprovalEngine approvals, IWorkNotificationEngine notifications,
        ISprintRepository sprints)
    {
        _hierarchy = hierarchy;
        _members = members;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _approvals = approvals;
        _notifications = notifications;
        _sprints = sprints;
    }

    public async Task<Result<Guid>> ResolveCreatorPositionAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        var tree = await _hierarchy.LoadTreeAsync(tenantId, projectId, ct);
        if (tree.Root is null)
            return Result<Guid>.NotFound("Objective not found.");

        var owned = tree.HighestOwnedModuleId(employeeId);
        if (owned is { } ownedId)
            return Result<Guid>.Success(ownedId);

        var memberModuleIds = await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, projectId, employeeId, ct);
        var memberOf = memberModuleIds
            .Where(id => tree.Get(id) is not null)
            .OrderBy(id => tree.AncestorChain(id).Count)
            .Select(id => (Guid?)id)
            .FirstOrDefault();

        return Result<Guid>.Success(memberOf ?? tree.Root.Id);
    }

    public async Task<Result<SprintActionOutcome>> SubmitAsync(
        SprintActionRequest action, Func<CancellationToken, Task<Result<Sprint?>>> apply, CancellationToken ct = default)
    {
        var tree = await _hierarchy.LoadTreeAsync(action.TenantId, action.ProjectId, ct);
        if (tree.Root is null)
            return Result<SprintActionOutcome>.NotFound("Objective not found.");

        var position = action.CreatorPositionObjectiveId ?? action.Sprint?.CreatorPositionObjectiveId ?? tree.Root.Id;
        var recipients = new List<Guid>();
        if (tree.Get(position)?.OwnerId is { } holder)
            recipients.Add(holder);
        if (action.Sprint is not null
            && await _identity.ResolveCallerEmployeeIdAsync(action.TenantId, action.Sprint.CreatedById, ct) is { } creatorEmployeeId)
            recipients.Add(creatorEmployeeId);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                if (!action.ForceDirect)
                {
                    var decision = await _approvals.SubmitAsync(new WorkAction(
                        action.TenantId, action.ProjectId, action.ActorEmployeeId,
                        action.ActionType, WorkTargetTypes.Sprint,
                        TargetId: action.Sprint?.Id, TargetTitle: action.Title,
                        TargetModuleId: tree.Root.Id, PositionModuleId: position,
                        PayloadJson: action.Input is null ? "{}" : JsonSerializer.Serialize(action.Input, action.Input.GetType(), SprintPayloadJson.Options),
                        TargetUpdatedAt: action.Sprint is null ? null : action.Sprint.UpdatedAt ?? action.Sprint.CreatedAt), innerCt);
                    if (!decision.IsSuccess)
                        return Result<SprintActionOutcome>.Failure(decision.Error!, decision.StatusCode ?? 400);

                    if (!decision.Value!.IsDirect)
                    {
                        await _sprints.SaveChangesAsync(innerCt);
                        return Result<SprintActionOutcome>.Success(new SprintActionOutcome(false, decision.Value.ApprovalRequestId, null));
                    }
                }

                var applied = await apply(innerCt);
                if (!applied.IsSuccess)
                    return Result<SprintActionOutcome>.Failure(applied.Error!, applied.StatusCode ?? 400);

                var sprint = applied.Value;
                await _notifications.NotifyAsync(new WorkNotificationEvent(
                    action.TenantId, action.ProjectId, action.ActorEmployeeId, WorkNotificationKinds.Direct,
                    action.ActionType, WorkTargetTypes.Sprint, sprint?.Id ?? action.Sprint?.Id, action.Title, null, recipients), innerCt);

                await _sprints.SaveChangesAsync(innerCt);
                return Result<SprintActionOutcome>.Success(new SprintActionOutcome(true, null, sprint));
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Two concurrent Start/Complete/Achieve/Edit/Delete requests (double-click or client
            // retry) both passed the in-memory status check; Sprint.Status is a concurrency token
            // (SprintConfiguration.cs), so the loser's conditional UPDATE affects 0 rows and EF
            // throws here instead of silently double-applying task moves/notifications.
            return Result<SprintActionOutcome>.Conflict(
                "This sprint was just updated by another request. Please refresh and try again.");
        }
    }
}
