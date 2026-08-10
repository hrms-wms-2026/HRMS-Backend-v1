namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;

/// <summary>Payload for the platform manager invite email outbox message.</summary>
public sealed record PlatformManagerInviteEmailPayload(string Email, string FullName, string RawToken);
