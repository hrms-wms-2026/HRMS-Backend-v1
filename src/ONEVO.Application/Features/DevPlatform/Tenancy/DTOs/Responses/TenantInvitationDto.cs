namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record TenantInvitationDto(
    Guid UserId,
    DateTimeOffset InviteExpiresAt);
