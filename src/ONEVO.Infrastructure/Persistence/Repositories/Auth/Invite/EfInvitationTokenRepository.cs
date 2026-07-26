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

    public EfInvitationTokenRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<InvitationToken?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var query = _db.InvitationTokens.Where(i => i.Id == id);
        var invitationToken = await query.FirstOrDefaultAsync(ct);
        return invitationToken;
    }

    public async Task<InvitationToken?> GetActiveByEmailAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.InvitationTokens.Where(
            i => i.TenantId == tenantId
              && i.InvitedEmail == normalizedEmail
              && i.UsedAt == null
              && i.RevokedAt == null
              && i.ExpiresAt > now);
        var invitationToken = await query.FirstOrDefaultAsync(ct);
        return invitationToken;
    }

    public async Task<int> CountActiveByTenantAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.InvitationTokens.Where(
            i => i.TenantId == tenantId
              && i.UsedAt == null
              && i.RevokedAt == null
              && i.ExpiresAt > now);
        var activeCount = await query.CountAsync(ct);
        return activeCount;
    }

    public async Task<bool> HasAcceptedInviteByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var query = _db.InvitationTokens.Where(
            i => i.TenantId == tenantId && i.UsedAt != null);
        var hasAcceptedInvite = await query.AnyAsync(ct);
        return hasAcceptedInvite;
    }

    public async Task<IReadOnlyList<InvitationToken>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var query = _db.InvitationTokens
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt);
        var invitationTokens = await query.ToListAsync(ct);
        return invitationTokens;
    }

    public async Task AddAsync(InvitationToken invitation, CancellationToken ct = default)
    {
        await _db.InvitationTokens.AddAsync(invitation, ct);
    }

    public async Task<InvitationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var query = _db.InvitationTokens.Where(i => i.TokenHash == tokenHash);
        var invitationToken = await query.FirstOrDefaultAsync(ct);
        return invitationToken;
    }
}
