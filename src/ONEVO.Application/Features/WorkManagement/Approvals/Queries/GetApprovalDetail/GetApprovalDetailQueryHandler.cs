using System.Globalization;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;

public sealed class GetApprovalDetailQueryHandler : IRequestHandler<GetApprovalDetailQuery, Result<ApprovalDetailResponse>>
{
    private const string NotFoundMessage = "Approval request not found.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IWorkTaskRepository _tasks;
    private readonly ISprintRepository _sprints;
    private readonly IProjectRepository _projects;

    public GetApprovalDetailQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests,
        IWorkHierarchyService hierarchy, IProjectMemberInvitationRepository invitations, IWorkTaskRepository tasks,
        ISprintRepository sprints, IProjectRepository projects)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
        _invitations = invitations;
        _tasks = tasks;
        _sprints = sprints;
        _projects = projects;
    }

    public async Task<Result<ApprovalDetailResponse>> Handle(GetApprovalDetailQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ApprovalDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ApprovalDetailResponse>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        return query.Source switch
        {
            ApprovalFeedSources.Engine => await EngineDetailAsync(tenantId, query.Id, caller, ct),
            ApprovalFeedSources.Invitation => await InvitationDetailAsync(tenantId, query.Id, caller, ct),
            _ => Result<ApprovalDetailResponse>.Failure("Source must be 'engine' or 'invitation'.")
        };
    }

    private async Task<Result<ApprovalDetailResponse>> EngineDetailAsync(Guid tenantId, Guid id, Guid caller, CancellationToken ct)
    {
        var request = await _requests.GetByIdForTenantAsync(tenantId, id, ct);
        if (request is null)
            return Result<ApprovalDetailResponse>.NotFound(NotFoundMessage);

        var tree = await _hierarchy.LoadTreeAsync(tenantId, request.ProjectId, ct);
        // Don't leak that the request exists to someone who is neither sender nor receiver.
        if (!ApprovalFeedParticipants.CanSee(tree, request, caller))
            return Result<ApprovalDetailResponse>.NotFound(NotFoundMessage);

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<ApprovalDetailResponse>.NotFound(NotFoundMessage);

        var payload = ApprovalPayloadJson.Parse(request.PayloadJson);
        var current = new Dictionary<string, string?>();
        var taskModules = new Dictionary<Guid, Guid>();
        var employeeIds = ApprovalFeedItemFactory.EmployeeIds(request).ToList();
        Guid? currentOwnerId = null, newOwnerId = null;

        if (request.TargetId is Guid targetId)
        {
            switch (request.TargetType)
            {
                case WorkTargetTypes.Task:
                    var task = await _tasks.GetByIdForTenantAsync(tenantId, targetId, ct);
                    if (task is not null)
                    {
                        taskModules[task.Id] = task.ObjectiveId;
                        current["title"] = task.Title;
                        current["description"] = task.Description;
                        current["priority"] = task.Priority;
                        current["dueDate"] = Date(task.DueDate);
                        current["estimatedHours"] = Number(task.EstimatedHours);
                        current["storyPoints"] = task.StoryPoints?.ToString(CultureInfo.InvariantCulture);
                        current["progressPercent"] = task.ProgressPercent.ToString(CultureInfo.InvariantCulture);
                    }
                    break;
                case WorkTargetTypes.Module:
                    var module = tree.Get(targetId);
                    if (module is not null)
                    {
                        current["title"] = module.Title;
                        current["description"] = module.Description;
                        current["startDate"] = Date(module.StartDate);
                        current["endDate"] = Date(module.EndDate);
                        current["allocatedHours"] = Number(module.AllocatedHours);
                        currentOwnerId = module.OwnerId;
                    }
                    break;
                case WorkTargetTypes.Sprint:
                    var sprint = await _sprints.GetByIdForTenantAsync(tenantId, targetId, ct);
                    if (sprint is not null)
                    {
                        current["name"] = sprint.Name;
                        current["goal"] = sprint.Goal;
                        current["startDate"] = Date(sprint.StartDate);
                        current["endDate"] = Date(sprint.EndDate);
                    }
                    break;
            }
        }

        if (request.ActionType == WorkActionTypes.ModuleTransfer)
        {
            newOwnerId = ApprovalPayloadJson.GetGuid(payload, "newHeadEmployeeId");
            if (currentOwnerId is Guid o) employeeIds.Add(o);
            if (newOwnerId is Guid n) employeeIds.Add(n);
        }

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds.Distinct().ToList(), ct);
        IReadOnlyDictionary<string, string?>? requestedOverrides = null;
        if (request.ActionType == WorkActionTypes.ModuleTransfer)
        {
            current["newHeadEmployeeId"] = currentOwnerId is Guid o ? names.GetValueOrDefault(o) : null;
            requestedOverrides = new Dictionary<string, string?>
            {
                ["newHeadEmployeeId"] = newOwnerId is Guid n ? names.GetValueOrDefault(n) ?? "Unknown employee" : null
            };
        }

        var lookup = new ApprovalFeedLookup(tree, project.Name, names, taskModules, new Dictionary<Guid, int>());
        var item = ApprovalFeedItemFactory.FromRequest(request, caller, lookup);
        var canEdit = item.CanDecide && ApprovalChangeSetBuilder.EditableActions.Contains(request.ActionType);
        var fields = ApprovalChangeSetBuilder.Build(
            request.ActionType, request.PayloadJson, request.AppliedPayloadJson, current, canEdit, requestedOverrides);
        var note = ApprovalPayloadJson.GetString(payload, "reason") ?? ApprovalPayloadJson.GetString(payload, "note");
        decimal? allocated = request.ActionType == WorkActionTypes.ModuleAllocationExtend && request.TargetId is Guid m
            ? tree.Get(m)?.AllocatedHours
            : null;

        return Result<ApprovalDetailResponse>.Success(new ApprovalDetailResponse(
            item, request.PayloadJson, request.AppliedPayloadJson, fields, canEdit,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(), allocated, Invitation: null));
    }

    private async Task<Result<ApprovalDetailResponse>> InvitationDetailAsync(Guid tenantId, Guid id, Guid caller, CancellationToken ct)
    {
        var invitation = await _invitations.GetByIdForTenantAsync(tenantId, id, ct);
        if (invitation is null || (invitation.InvitedEmployeeId != caller && invitation.InvitedById != caller))
            return Result<ApprovalDetailResponse>.NotFound(NotFoundMessage);

        var project = await _projects.GetByIdForTenantAsync(tenantId, invitation.ProjectId, ct);
        if (project is null)
            return Result<ApprovalDetailResponse>.NotFound(NotFoundMessage);

        var tree = await _hierarchy.LoadTreeAsync(tenantId, invitation.ProjectId, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, ApprovalFeedItemFactory.EmployeeIds(invitation).Distinct().ToList(), ct);
        var lookup = new ApprovalFeedLookup(tree, project.Name, names, new Dictionary<Guid, Guid>(), new Dictionary<Guid, int>());
        var item = ApprovalFeedItemFactory.FromInvitation(invitation, caller, lookup);

        var block = new ApprovalInvitationResponse(
            invitation.InvitedEmployeeId, item.ApproverName, invitation.InviteType,
            invitation.InvitedById, item.RequestedByName,
            invitation.ObjectiveId, item.ModuleTitle ?? "Module", invitation.ExpiresAt);

        return Result<ApprovalDetailResponse>.Success(new ApprovalDetailResponse(
            item, RequestedPayloadJson: null, AppliedPayloadJson: null, Fields: [], CanEditPayload: false,
            Note: null, CurrentAllocatedHours: null, block));
    }

    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Number(decimal? value) => value is decimal d ? ApprovalPayloadJson.FormatNumber(d) : null;
}
