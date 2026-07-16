using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;

public interface IInvitationTokenRepository
{
    Task<InvitationToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InvitationToken?> GetActiveByEmailAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<int> CountActiveByTenantAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken ct = default);
    Task<bool> HasAcceptedInviteByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);
    Task<IReadOnlyList<InvitationToken>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default);
    Task AddAsync(InvitationToken invitation, CancellationToken ct = default);
    Task<InvitationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
}
