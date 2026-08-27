namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveMemberItemResponse(
    Guid EmployeeId, string? Name, bool IsHead, bool Pending, string? InviteType, Guid? InvitationId, DateTimeOffset SinceOrInvitedAt);
