using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfLoginWorkspaceSelectionChallengeRepository : ILoginWorkspaceSelectionChallengeRepository
{
    private const int MaximumFailedAttempts = 5;

    private readonly ApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;

    public EfLoginWorkspaceSelectionChallengeRepository(
        ApplicationDbContext db,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<string> CreateAsync(
        string normalizedEmail,
        IReadOnlyList<WorkspaceCandidateSnapshot> candidates,
        string? ipAddress,
        string? userAgent,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        return await CreateAsync(
            normalizedEmail,
            "password",
            candidates,
            ipAddress,
            userAgent,
            lifetime,
            ct);
    }

    public async Task<string> CreateAsync(
        string normalizedEmail,
        string origin,
        IReadOnlyList<WorkspaceCandidateSnapshot> candidates,
        string? ipAddress,
        string? userAgent,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var staleChallenges = await _db.LoginWorkspaceSelectionChallenges
            .Where(c => c.NormalizedEmail == normalizedEmail
                && (c.ExpiresAt <= now || c.ConsumedAt != null))
            .ToListAsync(ct);
        if (staleChallenges.Count > 0)
        {
            _db.LoginWorkspaceSelectionChallenges.RemoveRange(staleChallenges);
        }

        var rawChallenge = _tokens.GenerateOpaqueToken();
        var payloadJson = JsonSerializer.Serialize(
            new WorkspaceSelectionPayload(origin, candidates));

        var challenge = new LoginWorkspaceSelectionChallenge
        {
            Id = Guid.NewGuid(),
            ChallengeHash = _tokens.HashToken(rawChallenge),
            NormalizedEmail = normalizedEmail,
            CandidateWorkspacesJson = payloadJson,
            Purpose = "workspace_selection",
            ExpiresAt = now.Add(lifetime),
            ConsumedAt = null,
            FailedAttemptCount = 0,
            CreatedAt = now,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _db.LoginWorkspaceSelectionChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);

        return rawChallenge;
    }

    public async Task<LoginWorkspaceSelectionChallengeState?> GetActiveAsync(
        string rawChallenge,
        CancellationToken ct = default)
    {
        var row = await FindActiveRowAsync(rawChallenge, ct);
        if (row is null)
            return null;

        return ToState(row);
    }

    public async Task<bool> RegisterFailedAttemptAsync(
        string rawChallenge,
        int maximumAttempts,
        CancellationToken ct = default)
    {
        var challengeHash = _tokens.HashToken(rawChallenge);
        var now = _clock.UtcNow;

        // Single atomic UPDATE mirrors PostgresMfaChallengeStore.RegisterFailedAttemptAsync: the
        // database increments failed_attempt_count from its current stored value and, in the
        // same statement, sets consumed_at when the new count reaches the maximum, so parallel
        // invalid selections cannot lose increments or exceed the limit.
        var updatedRows = await _db.LoginWorkspaceSelectionChallenges
            .Where(c => c.ChallengeHash == challengeHash)
            .Where(c => c.ConsumedAt == null)
            .Where(c => c.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        c => c.FailedAttemptCount,
                        c => c.FailedAttemptCount + 1)
                    .SetProperty(
                        c => c.ConsumedAt,
                        c => c.FailedAttemptCount + 1 >= maximumAttempts
                            ? (DateTimeOffset?)now
                            : c.ConsumedAt),
                ct);

        if (updatedRows == 0)
            return false;

        DetachTrackedChallenge(challengeHash);

        var updatedRow = await _db.LoginWorkspaceSelectionChallenges
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChallengeHash == challengeHash, ct);

        if (updatedRow is null)
            return false;

        if (updatedRow.ConsumedAt is not null)
            return false;

        if (updatedRow.ExpiresAt <= now)
            return false;

        return true;
    }

    public async Task<LoginWorkspaceSelectionChallengeState?> TryConsumeAsync(
        string rawChallenge,
        CancellationToken ct = default)
    {
        var row = await FindActiveRowAsync(rawChallenge, ct);
        if (row is null)
            return null;

        var state = ToState(row);
        row.ConsumedAt = _clock.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request consumed this challenge first; it is single-use, so losing
            // the race must return null rather than proceed with a stale snapshot.
            return null;
        }

        return state;
    }

    private void DetachTrackedChallenge(string challengeHash)
    {
        foreach (var entry in _db.ChangeTracker.Entries<LoginWorkspaceSelectionChallenge>())
        {
            if (entry.Entity.ChallengeHash == challengeHash)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<LoginWorkspaceSelectionChallenge?> FindActiveRowAsync(
        string rawChallenge,
        CancellationToken ct)
    {
        var challengeHash = _tokens.HashToken(rawChallenge);
        var now = _clock.UtcNow;

        var row = await _db.LoginWorkspaceSelectionChallenges
            .FirstOrDefaultAsync(c => c.ChallengeHash == challengeHash, ct);

        if (row is null)
            return null;

        if (row.ConsumedAt is not null)
            return null;

        if (row.ExpiresAt <= now)
            return null;

        return row;
    }

    private static LoginWorkspaceSelectionChallengeState ToState(LoginWorkspaceSelectionChallenge row)
    {
        var payload = JsonSerializer.Deserialize<WorkspaceSelectionPayload>(row.CandidateWorkspacesJson);
        if (payload is not null)
        {
            return new LoginWorkspaceSelectionChallengeState(
                row.NormalizedEmail,
                payload.Origin,
                payload.Candidates,
                row.ExpiresAt,
                row.FailedAttemptCount);
        }

        // Backward compatibility for challenge rows created before origin was added to the
        // server-only JSON payload. Those rows were created only by the password flow.
        var candidates = JsonSerializer.Deserialize<List<WorkspaceCandidateSnapshot>>(
            row.CandidateWorkspacesJson) ?? [];
        return new LoginWorkspaceSelectionChallengeState(
            row.NormalizedEmail,
            "password",
            candidates,
            row.ExpiresAt,
            row.FailedAttemptCount);
    }

    private sealed record WorkspaceSelectionPayload(
        string Origin,
        IReadOnlyList<WorkspaceCandidateSnapshot> Candidates);
}
