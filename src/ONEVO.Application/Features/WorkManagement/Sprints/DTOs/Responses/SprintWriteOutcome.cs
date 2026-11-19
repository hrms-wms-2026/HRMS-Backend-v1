namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

/// <summary>Exactly one of Sprint / ApprovalRequestId is set (both null = a delete that was applied).</summary>
public sealed record SprintWriteOutcome(SprintResponse? Sprint, Guid? ApprovalRequestId);
