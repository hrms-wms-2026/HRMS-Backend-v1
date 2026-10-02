namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

/// <summary>Applied - the member was removed (or their invite cancelled) now. Otherwise ApprovalRequestId is set.</summary>
public sealed record RemoveObjectiveMemberOutcomeResponse(bool Applied, Guid? ApprovalRequestId);
