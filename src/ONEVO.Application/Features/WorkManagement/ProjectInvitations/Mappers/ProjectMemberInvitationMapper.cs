using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Mappers;

public static class ProjectMemberInvitationMapper
{
    public static ProjectMemberInvitationResponse ToResponse(ProjectMemberInvitation invitation) => new(
        invitation.Id, invitation.ProjectId, invitation.ObjectiveId, invitation.InvitedEmployeeId, invitation.InviteType,
        invitation.Status, invitation.InvitedById, invitation.DecidedAt, invitation.CreatedAt);
}
