using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;

public class EfInvitationTokenRepository : IInvitationTokenRepository
{
    private readonly ApplicationDbContext _db;

    public EfInvitationTokenRepository(ApplicationDbContext db) => _db = db;

    public Task<InvitationToken?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.InvitationTokens.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<InvitationToken?> GetActiveByEmailAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        _db.InvitationTokens.FirstOrDefaultAsync(
            i => i.TenantId == tenantId
              && i.InvitedEmail == normalizedEmail
              && i.UsedAt == null
              && i.RevokedAt == null
              && i.ExpiresAt > now,
            ct);

    public Task<int> CountActiveByTenantAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        _db.InvitationTokens.CountAsync(
            i => i.TenantId == tenantId
              && i.UsedAt == null
              && i.RevokedAt == null
              && i.ExpiresAt > now,
            ct);

    public Task<bool> HasAcceptedInviteByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default) =>
        _db.InvitationTokens.AnyAsync(
            i => i.TenantId == tenantId && i.UsedAt != null,
            ct);

    public async Task<IReadOnlyList<InvitationToken>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default) =>
        await _db.InvitationTokens
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(InvitationToken invitation, CancellationToken ct = default) =>
        await _db.InvitationTokens.AddAsync(invitation, ct);

    public Task<InvitationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.InvitationTokens.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);
}
