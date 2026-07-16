namespace ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;

/// <summary>
/// Internal DTO carrying everything needed to send the tenant-owner invite email.
/// Created by <see cref="ITenantOwnerInvitationService.CreateInviteRecordsAsync"/>
/// (before SaveChanges) and consumed by
/// <see cref="ITenantOwnerInvitationService.TrySendInviteEmailAsync"/> (after SaveChanges).
/// The plaintext token lives only in this DTO and the outbound email; it is never persisted.
/// </summary>
public sealed record PendingOwnerInvite(
    Guid UserId,
    string Email,
    string FullName,
    string PlaintextToken,
    DateTimeOffset ExpiresAt,
    Guid TenantId,
    string TenantName,
    string FirstName);
