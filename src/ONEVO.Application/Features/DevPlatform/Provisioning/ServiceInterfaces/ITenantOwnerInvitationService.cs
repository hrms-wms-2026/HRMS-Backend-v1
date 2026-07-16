using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;

public interface ITenantOwnerInvitationService
{
    /// <summary>
    /// Standalone path: validates, looks up/creates the Owner role, creates invite records,
    /// SaveChanges, and sends email. For use outside the CreateTenant orchestration.
    /// </summary>
    Task<Result<TenantInvitationDto>> InviteOwnerAsync(
        Guid tenantId,
        TenantOwnerInviteRequest request,
        CancellationToken ct);

    /// <summary>
    /// Orchestration path: creates User, UserRole, and InvitationToken entities in the
    /// DbContext using the supplied (already-seeded) Owner role id. Does NOT call
    /// SaveChanges and does NOT send email. The caller invokes
    /// <see cref="QueueInviteEmailAsync"/> and then commits the surrounding transaction.
    /// </summary>
    Task<Result<PendingOwnerInvite>> CreateInviteRecordsAsync(
        Guid tenantId,
        Guid roleId,
        TenantOwnerInviteRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues the invite email as an outbox message in the current unit of work.
    /// Does NOT call SaveChanges: the caller's commit persists the invite records and
    /// the email side effect atomically; the outbox worker delivers it afterwards.
    /// </summary>
    Task QueueInviteEmailAsync(
        PendingOwnerInvite pending,
        CancellationToken ct = default);
}
