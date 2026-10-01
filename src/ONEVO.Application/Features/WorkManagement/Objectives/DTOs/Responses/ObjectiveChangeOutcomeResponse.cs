namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

/// <summary>Applied, or sent for approval (ApprovalRequestId is the wm_approval_requests row).</summary>
public sealed record ObjectiveChangeOutcomeResponse(bool Applied, Guid? ApprovalRequestId);
