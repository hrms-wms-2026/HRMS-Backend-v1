using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record TransferOutcomeResponse(
    bool Applied, ObjectiveChangeRequestResponse? PendingChangeRequest, ProjectMemberInvitationResponse? PendingInvitation);
