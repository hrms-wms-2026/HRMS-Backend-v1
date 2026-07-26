using System.Data;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;

public sealed class EfLegalLoginChallengeRepository : ILegalLoginChallengeRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly IWritableTenantContext _tenantContext;

    public EfLegalLoginChallengeRepository(
        ApplicationDbContext db,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        IWritableTenantContext tenantContext)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _tenantContext = tenantContext;
    }

    public async Task<(string RawChallenge, string RawCsrfToken)> CreateAsync(
        Guid tenantId,
        Guid userId,
        string origin,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var rawChallenge = _tokens.GenerateOpaqueToken();
        var rawCsrfToken = _tokens.GenerateCsrfToken();

        var challenge = new LegalLoginChallenge
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChallengeHash = _tokens.HashToken(rawChallenge),
            CsrfTokenHash = _tokens.HashToken(rawCsrfToken),
            Origin = origin,
            ExpiresAt = now.Add(lifetime),
            SupersededAt = null,
            SupersededById = null,
            ConsumedAt = null,
            CreatedAt = now
        };

        _db.LegalLoginChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);

        return (rawChallenge, rawCsrfToken);
    }

    public async Task<LegalLoginChallengeState?> GetActiveAsync(string rawChallenge, CancellationToken ct = default)
    {
        var row = await FindActiveRowAsync(rawChallenge, ct);
        return row is null ? null : ToState(row);
    }

    public async Task<LegalLoginChallengeState?> GetActiveForPreTenantContinuationAsync(
        string rawChallenge,
        CancellationToken ct = default)
    {
        if (_tenantContext.ContextMode == TenantContextMode.Tenant)
        {
            throw new InvalidOperationException(
                "Pre-tenant legal challenge lookup cannot run from an already resolved tenant context.");
        }

        var previousMode = _tenantContext.ContextMode;
        await CloseConnectionIfOpenAsync();
        _tenantContext.SetAdminMode();

        try
        {
            var row = await FindActiveRowAsync(rawChallenge, ct);
            return row is null ? null : ToState(row);
        }
        finally
        {
            // TenantRlsInterceptor applies context only when a connection opens. Ensure no
            // admin-configured connection survives this narrow opaque-token lookup boundary.
            await CloseConnectionIfOpenAsync();
            if (previousMode == TenantContextMode.Admin)
                _tenantContext.SetAdminMode();
            else
                _tenantContext.SetSystemMode();
        }
    }

    public async Task<LegalChallengeCommitResult> TryCommitAcceptancesAsync(
        string rawChallenge,
        LegalLoginChallengeState expectedState,
        bool isComplete,
        TimeSpan replacementLifetime,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var challengeHash = _tokens.HashToken(rawChallenge);
        string? replacementRawChallenge = null;
        string? replacementRawCsrf = null;
        LegalLoginChallenge? replacement = null;

        if (!isComplete)
        {
            replacementRawChallenge = _tokens.GenerateOpaqueToken();
            replacementRawCsrf = _tokens.GenerateCsrfToken();
            replacement = new LegalLoginChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = expectedState.TenantId,
                UserId = expectedState.UserId,
                ChallengeHash = _tokens.HashToken(replacementRawChallenge),
                CsrfTokenHash = _tokens.HashToken(replacementRawCsrf),
                Origin = expectedState.Origin,
                ExpiresAt = now.Add(replacementLifetime),
                CreatedAt = now
            };
        }

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            if (replacement is not null)
            {
                _db.LegalLoginChallenges.Add(replacement);
                // The replacement must exist before the atomic superseding UPDATE assigns its
                // foreign key. Staged acceptance inserts are part of this same transaction and
                // are rolled back if this request loses the challenge race below.
                await _db.SaveChangesAsync(ct);
            }

            var active = _db.LegalLoginChallenges
                .Where(c => c.ChallengeHash == challengeHash)
                .Where(c => c.TenantId == expectedState.TenantId && c.UserId == expectedState.UserId)
                .Where(c => c.ConsumedAt == null && c.SupersededAt == null && c.ExpiresAt > now);

            var won = isComplete
                ? await active.ExecuteUpdateAsync(
                    setters => setters.SetProperty(c => c.ConsumedAt, _ => now), ct)
                : await active.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(c => c.SupersededAt, _ => now)
                        .SetProperty(c => c.SupersededById, _ => replacement!.Id),
                    ct);

            if (won != 1)
            {
                await transaction.RollbackAsync(ct);
                return new LegalChallengeCommitResult(false);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new LegalChallengeCommitResult(
                true,
                replacementRawChallenge,
                replacementRawCsrf);
        });
    }

    private async Task<LegalLoginChallenge?> FindActiveRowAsync(string rawChallenge, CancellationToken ct)
    {
        var challengeHash = _tokens.HashToken(rawChallenge);
        var now = _clock.UtcNow;

        return await _db.LegalLoginChallenges
            .AsNoTracking()
            .Where(c => c.ChallengeHash == challengeHash)
            .Where(c => c.ConsumedAt == null && c.SupersededAt == null)
            .Where(c => c.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);
    }

    private async Task CloseConnectionIfOpenAsync()
    {
        if (_db.Database.GetDbConnection().State == ConnectionState.Open)
            await _db.Database.CloseConnectionAsync();
    }

    private static LegalLoginChallengeState ToState(LegalLoginChallenge row) =>
        new(row.TenantId, row.UserId, row.Origin, row.CsrfTokenHash, row.ExpiresAt);
}
