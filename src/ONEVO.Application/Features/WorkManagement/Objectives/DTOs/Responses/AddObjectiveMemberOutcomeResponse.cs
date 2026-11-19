using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

/// <summary>Applied - the invitation was created now (caller was the parent owner or above, or AlreadyMember
/// is true and there's nothing to do). Otherwise ApprovalRequestId is set and Invitation is null.</summary>
public sealed record AddObjectiveMemberOutcomeResponse(bool Applied, bool AlreadyMember, Guid? ApprovalRequestId, ProjectMemberInvitationResponse? Invitation);
