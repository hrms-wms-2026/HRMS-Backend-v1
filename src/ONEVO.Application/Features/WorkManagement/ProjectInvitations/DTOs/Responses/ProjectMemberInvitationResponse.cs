namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.DTOs.Responses;

public sealed record ProjectMemberInvitationResponse(
    Guid Id, Guid ProjectId, Guid ObjectiveId, Guid InvitedEmployeeId, string InviteType,
    string Status, Guid InvitedById, DateTimeOffset? DecidedAt, DateTimeOffset CreatedAt);
