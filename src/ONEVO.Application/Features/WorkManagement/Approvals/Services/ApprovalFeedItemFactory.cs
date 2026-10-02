using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Everything the factory needs that must be loaded first. TaskModules maps a task id to its
/// module; CommentCounts maps a subject id (request or invitation) to its comment count.</summary>
internal sealed record ApprovalFeedLookup(
    ProjectModuleTree Tree,
    string ProjectName,
    IReadOnlyDictionary<Guid, string> Names,
    IReadOnlyDictionary<Guid, Guid> TaskModules,
    IReadOnlyDictionary<Guid, int> CommentCounts);

/// <summary>Builds Approvals-page rows for engine requests and module invitations. Shared by the feed and
/// detail queries so a row looks the same in the list and in the explanation card.</summary>
internal static class ApprovalFeedItemFactory
{
    public const string InvitationActionType = "module.invitation";
    public const string Sent = "sent";
    public const string Received = "received";

    /// <summary>Task ids whose module has to be looked up (task targets other than create).</summary>
    public static IReadOnlyList<Guid> TaskIdsNeedingModule(IEnumerable<WorkApprovalRequest> requests)
        => requests.Where(r => r.TargetType == WorkTargetTypes.Task && r.ActionType != WorkActionTypes.TaskCreate && r.TargetId is not null)
            .Select(r => r.TargetId!.Value).Distinct().ToList();

    public static IEnumerable<Guid> EmployeeIds(WorkApprovalRequest r)
        => r.DecidedByEmployeeId is Guid d
            ? new[] { r.RequestedByEmployeeId, r.ApproverEmployeeId, d }
            : new[] { r.RequestedByEmployeeId, r.ApproverEmployeeId };

    public static IEnumerable<Guid> EmployeeIds(ProjectMemberInvitation i) => new[] { i.InvitedById, i.InvitedEmployeeId };

    public static ApprovalFeedItemResponse FromRequest(WorkApprovalRequest r, Guid caller, ApprovalFeedLookup lookup)
    {
        var payload = ApprovalPayloadJson.Parse(r.PayloadJson);
        var moduleId = ResolveModuleId(r, payload, lookup);
        var isSender = ApprovalFeedParticipants.IsSender(r, caller);
        var pending = r.Status == WorkApprovalRequestStatuses.Pending;

        return new ApprovalFeedItemResponse(
            r.Id, ApprovalFeedSources.Engine, r.ActionType, r.TargetType, r.TargetId, r.TargetTitle,
            moduleId, ModuleTitle(moduleId, lookup), lookup.ProjectName, r.Status,
            isSender ? Sent : Received,
            r.RequestedByEmployeeId, Name(r.RequestedByEmployeeId, lookup),
            r.ApproverEmployeeId, Name(r.ApproverEmployeeId, lookup),
            r.DecidedByEmployeeId, r.DecidedByEmployeeId is Guid d ? Name(d, lookup) : null,
            r.DecisionComment, r.CreatedAt, r.DecidedAt,
            CanDecide: pending && WorkApprovalDecisionRules.CanDecide(lookup.Tree, r, caller),
            CanCancel: pending && isSender,
            lookup.CommentCounts.GetValueOrDefault(r.Id),
            ApprovalSummaries.For(r, payload));
    }

    public static ApprovalFeedItemResponse FromInvitation(ProjectMemberInvitation i, Guid caller, ApprovalFeedLookup lookup)
    {
        var status = MapInvitationStatus(i.Status);
        var decided = i.Status is ProjectInvitationStatuses.Accepted or ProjectInvitationStatuses.Declined;
        var moduleTitle = ModuleTitle(i.ObjectiveId, lookup);

        return new ApprovalFeedItemResponse(
            i.Id, ApprovalFeedSources.Invitation, InvitationActionType, WorkTargetTypes.Module, i.ObjectiveId,
            moduleTitle ?? "Module", i.ObjectiveId, moduleTitle, lookup.ProjectName, status,
            i.InvitedById == caller ? Sent : Received,
            i.InvitedById, Name(i.InvitedById, lookup),
            i.InvitedEmployeeId, Name(i.InvitedEmployeeId, lookup),
            decided ? i.InvitedEmployeeId : null, decided ? Name(i.InvitedEmployeeId, lookup) : null,
            DecisionComment: null, i.CreatedAt, i.DecidedAt,
            CanDecide: status == WorkApprovalRequestStatuses.Pending && i.InvitedEmployeeId == caller,
            CanCancel: false,
            lookup.CommentCounts.GetValueOrDefault(i.Id),
            $"Invited as {i.InviteType}");
    }

    /// <summary>D2: invitation statuses mapped onto the engine vocabulary.</summary>
    public static string MapInvitationStatus(string status) => status switch
    {
        ProjectInvitationStatuses.Accepted => WorkApprovalRequestStatuses.Approved,
        ProjectInvitationStatuses.Declined => WorkApprovalRequestStatuses.Rejected,
        ProjectInvitationStatuses.Expired => WorkApprovalRequestStatuses.Stale,
        ProjectInvitationStatuses.Cancelled => WorkApprovalRequestStatuses.Cancelled,
        _ => WorkApprovalRequestStatuses.Pending
    };

    /// <summary>Pending first, then newest first.</summary>
    public static IReadOnlyList<ApprovalFeedItemResponse> Sort(IEnumerable<ApprovalFeedItemResponse> items)
        => items.OrderBy(i => i.Status == WorkApprovalRequestStatuses.Pending ? 0 : 1)
            .ThenByDescending(i => i.CreatedAt)
            .ToList();

    private static Guid? ResolveModuleId(WorkApprovalRequest r, System.Text.Json.JsonElement? payload, ApprovalFeedLookup lookup)
    {
        if (r.TargetType == WorkTargetTypes.Module)
            return r.TargetId ?? r.PositionObjectiveId;
        if (r.TargetType == WorkTargetTypes.Task)
        {
            if (r.ActionType == WorkActionTypes.TaskCreate)
                return ApprovalPayloadJson.GetGuid(payload, "objectiveId") ?? r.PositionObjectiveId;
            // A task missing from the lookup was deleted - fall back to the position module.
            return r.TargetId is Guid taskId && lookup.TaskModules.TryGetValue(taskId, out var moduleId)
                ? moduleId
                : r.PositionObjectiveId;
        }
        return r.PositionObjectiveId;
    }

    private static string? ModuleTitle(Guid? moduleId, ApprovalFeedLookup lookup)
    {
        if (moduleId is null) return null;
        var module = lookup.Tree.Get(moduleId.Value);
        if (module is null) return null;
        return module.IsDefault ? lookup.ProjectName : module.Title;
    }

    private static string Name(Guid employeeId, ApprovalFeedLookup lookup)
        => lookup.Names.GetValueOrDefault(employeeId) ?? "Unknown employee";
}
