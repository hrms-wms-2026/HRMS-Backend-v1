using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public sealed class EfWorkApprovalHistoryRepository : IWorkApprovalHistoryRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkApprovalHistoryRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkApprovalHistoryRecord>> ListForEmployeeAsync(
        Guid tenantId,
        Guid projectId,
        Guid employeeId,
        CancellationToken ct = default)
    {
        var engineRequests = await _db.WorkApprovalRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId
                && (r.ActionType.StartsWith("task.") || r.ActionType.StartsWith("module.") || r.ActionType.StartsWith("sprint.")
                    || r.ActionType == WorkActionTypes.ProjectStatusTemplateChange)
                && (r.RequestedByEmployeeId == employeeId || r.DecidedByEmployeeId == employeeId
                    || (r.Status == WorkApprovalRequestStatuses.Pending && r.ApproverEmployeeId == employeeId)))
            .ToListAsync(ct);
        var engineRecords = engineRequests.Select(r => new WorkApprovalHistoryRecord(
            r.Id,
            r.TargetType == WorkTargetTypes.Module ? r.TargetId ?? Guid.Empty : r.PositionObjectiveId ?? Guid.Empty,
            r.ActionType switch
            {
                WorkActionTypes.TaskCreate => "task_creation",
                WorkActionTypes.TaskEdit => "task_edit",
                WorkActionTypes.TaskDelete => "task_delete",
                WorkActionTypes.ProjectStatusTemplateChange => "task_status_change",
                WorkActionTypes.ModuleEdit => "objective_edit",
                WorkActionTypes.ModuleAllocationExtend => "allocation_extend",
                var a when a.StartsWith("module.") => "objective_change",
                var a when a.StartsWith("sprint.") => "sprint_change",
                _ => r.ActionType
            },
            r.Status,
            r.ActionType == WorkActionTypes.ProjectStatusTemplateChange ? "Task statuses" : r.TargetTitle,
            r.PayloadJson, r.RequestedByEmployeeId,
            r.DecidedByEmployeeId ?? r.ApproverEmployeeId, r.DecidedByEmployeeId, r.DecisionComment,
            r.CreatedAt, r.DecidedAt)).ToList();

        var invitations = await (
            from invitation in _db.ProjectMemberInvitations.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on invitation.ObjectiveId equals objective.Id
            where invitation.TenantId == tenantId
                  && invitation.ProjectId == projectId
                  && (invitation.InvitedById == employeeId || invitation.InvitedEmployeeId == employeeId)
            select new WorkApprovalHistoryRecord(
                invitation.Id,
                invitation.ObjectiveId,
                "objective_invitation",
                invitation.Status,
                objective.Title,
                null,
                invitation.InvitedById,
                invitation.InvitedEmployeeId,
                invitation.Status == ProjectInvitationStatuses.Pending
                    ? null
                    : invitation.InvitedEmployeeId,
                null,
                invitation.CreatedAt,
                invitation.DecidedAt)
        ).ToListAsync(ct);

        // Invitation payload is synthesized here because the entity stores invite type as a column,
        // while the shared history projection deliberately exposes one optional payload field.
        if (invitations.Count > 0)
        {
            var invitationIds = invitations.Select(item => item.Id).ToList();
            var invitationTypes = await _db.ProjectMemberInvitations
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId && invitationIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.InviteType, ct);
            invitations = invitations.Select(item => item with
            {
                PayloadJson = JsonSerializer.Serialize(new
                {
                    inviteType = invitationTypes.GetValueOrDefault(item.Id)
                })
            }).ToList();
        }

        return engineRecords
            .Concat(invitations)
            .OrderByDescending(item => item.DecidedAt ?? item.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<WorkApprovalHistoryRecord>> ListRequestedByEmployeeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default)
    {
        var taskCreation = await (
            from request in _db.TaskCreationRequests.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on request.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && request.RequestedByEmployeeId == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id, request.ObjectiveId, "task_creation", request.Status, objective.Title, request.PayloadJson,
                request.RequestedByEmployeeId, request.DecidedByEmployeeId ?? objective.OwnerId,
                request.DecidedByEmployeeId, request.DecisionComment, request.CreatedAt, request.DecidedAt)
        ).ToListAsync(ct);

        var taskEdits = await (
            from request in _db.TaskEditRequests.AsNoTracking()
            join task in _db.WorkTasks.AsNoTracking() on request.TaskId equals task.Id
            join objective in _db.Objectives.AsNoTracking() on task.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && request.RequestedByEmployeeId == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id, task.ObjectiveId, "task_edit", request.Status, task.Title, request.PayloadJson,
                request.RequestedByEmployeeId, request.DecidedByEmployeeId ?? objective.OwnerId,
                request.DecidedByEmployeeId, request.DecisionComment, request.CreatedAt, request.DecidedAt)
        ).ToListAsync(ct);

        var objectiveChanges = await (
            from request in _db.ObjectiveChangeRequests.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on request.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && request.RequestedById == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id,
                request.ObjectiveId,
                request.RequestType == ObjectiveChangeRequestTypes.ExtendAllocation
                    ? "allocation_extend"
                    : request.RequestType == ObjectiveChangeRequestTypes.Edit
                        ? "objective_edit"
                        : "objective_change",
                request.Status,
                objective.Title,
                request.PayloadJson,
                request.RequestedById,
                request.ReportingManagerId,
                // Same convention as ListForEmployeeAsync: DecidedById historically holds a UserId for
                // objective changes, so the reporting manager (the only actor allowed to decide) is the
                // reliable decision actor once the request is no longer pending.
                request.Status == ObjectiveChangeRequestStatuses.Pending ? null : request.ReportingManagerId,
                null,
                request.CreatedAt,
                request.DecidedAt)
        ).ToListAsync(ct);

        var statusChanges = await (
            from request in _db.TaskStatusChangeRequests.AsNoTracking()
            join root in _db.Objectives.AsNoTracking() on request.ProjectId equals root.ProjectId
            where request.TenantId == tenantId
                  && root.IsDefault
                  && request.RequestedByEmployeeId == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id, root.Id, "task_status_change", request.Status, "Task statuses", request.ChangesJson,
                request.RequestedByEmployeeId, request.DecidedByEmployeeId ?? root.OwnerId,
                request.DecidedByEmployeeId, request.DecisionComment, request.CreatedAt, request.DecidedAt)
        ).ToListAsync(ct);

        var invitations = await (
            from invitation in _db.ProjectMemberInvitations.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on invitation.ObjectiveId equals objective.Id
            where invitation.TenantId == tenantId
                  && invitation.InvitedEmployeeId == employeeId
                  && invitation.CreatedAt >= fromUtc && invitation.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                invitation.Id, invitation.ObjectiveId, "objective_invitation", invitation.Status, objective.Title, null,
                invitation.InvitedById, invitation.InvitedEmployeeId,
                invitation.Status == ProjectInvitationStatuses.Pending ? null : invitation.InvitedEmployeeId,
                null, invitation.CreatedAt, invitation.DecidedAt)
        ).ToListAsync(ct);

        return taskCreation
            .Concat(taskEdits)
            .Concat(objectiveChanges)
            .Concat(statusChanges)
            .Concat(invitations)
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
    }
}
