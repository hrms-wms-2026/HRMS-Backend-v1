using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.ProjectInvitations;

public static class ProjectMemberInvitationViewModelMapper
{
    public static ProjectMemberInvitationViewModel ToViewModel(this ProjectMemberInvitationResponse response) => new()
    {
        Id = response.Id, ProjectId = response.ProjectId, ObjectiveId = response.ObjectiveId,
        InvitedEmployeeId = response.InvitedEmployeeId, InviteType = response.InviteType, Status = response.Status,
        InvitedById = response.InvitedById, DecidedAt = response.DecidedAt, CreatedAt = response.CreatedAt
    };
}
