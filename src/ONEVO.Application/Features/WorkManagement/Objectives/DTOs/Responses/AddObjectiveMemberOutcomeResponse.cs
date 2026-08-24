using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record AddObjectiveMemberOutcomeResponse(bool AlreadyMember, ProjectMemberInvitationResponse? Invitation);
