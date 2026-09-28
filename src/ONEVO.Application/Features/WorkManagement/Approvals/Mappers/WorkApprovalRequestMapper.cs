using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Mappers;

public static class WorkApprovalRequestMapper
{
    public static WorkApprovalRequestResponse ToResponse(WorkApprovalRequest r, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.ProjectId, r.ActionType, r.TargetType, r.TargetId, r.TargetTitle, r.Status,
        r.RequestedByEmployeeId, names.GetValueOrDefault(r.RequestedByEmployeeId) ?? "Unknown",
        r.ApproverEmployeeId, names.GetValueOrDefault(r.ApproverEmployeeId) ?? "Unknown",
        r.PayloadJson, r.DecisionComment, r.CreatedAt, r.DecidedAt);
}
