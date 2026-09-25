using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
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
        var taskCreation = await (
            from request in _db.TaskCreationRequests.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on request.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && objective.ProjectId == projectId
                  && (request.RequestedByEmployeeId == employeeId
                      || request.DecidedByEmployeeId == employeeId
                      || (request.Status == TaskCreationRequestStatuses.Pending && objective.OwnerId == employeeId))
            select new WorkApprovalHistoryRecord(
                request.Id,
                request.ObjectiveId,
                "task_creation",
                request.Status,
                objective.Title,
                request.PayloadJson,
                request.RequestedByEmployeeId,
                request.DecidedByEmployeeId ?? objective.OwnerId,
                request.DecidedByEmployeeId,
                request.DecisionComment,
                request.CreatedAt,
                request.DecidedAt)
        ).ToListAsync(ct);

        var taskEdits = await (
            from request in _db.TaskEditRequests.AsNoTracking()
            join task in _db.WorkTasks.AsNoTracking() on request.TaskId equals task.Id
            join objective in _db.Objectives.AsNoTracking() on task.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && task.ProjectId == projectId
                  && (request.RequestedByEmployeeId == employeeId
                      || request.DecidedByEmployeeId == employeeId
                      || (request.Status == TaskEditRequestStatuses.Pending && objective.OwnerId == employeeId))
            select new WorkApprovalHistoryRecord(
                request.Id,
                task.ObjectiveId,
                "task_edit",
                request.Status,
                task.Title,
                request.PayloadJson,
                request.RequestedByEmployeeId,
                request.DecidedByEmployeeId ?? objective.OwnerId,
                request.DecidedByEmployeeId,
                request.DecisionComment,
                request.CreatedAt,
                request.DecidedAt)
        ).ToListAsync(ct);

        var objectiveChanges = await (
            from request in _db.ObjectiveChangeRequests.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on request.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && objective.ProjectId == projectId
                  && (request.RequestedById == employeeId
                      || request.ReportingManagerId == employeeId)
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
                // Objective-change commands historically persisted the authenticated UserId in
                // DecidedById, while RequestedById/ReportingManagerId are EmployeeIds. The command
                // authorizes only ReportingManagerId to act, so that employee snapshot is the
                // reliable decision actor for both existing and new rows.
                request.Status == ObjectiveChangeRequestStatuses.Pending
                    ? null
                    : request.ReportingManagerId,
                null,
                request.CreatedAt,
                request.DecidedAt)
        ).ToListAsync(ct);

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

        return taskCreation
            .Concat(taskEdits)
            .Concat(objectiveChanges)
            .Concat(invitations)
            .OrderByDescending(item => item.DecidedAt ?? item.CreatedAt)
            .ToList();
    }
}
