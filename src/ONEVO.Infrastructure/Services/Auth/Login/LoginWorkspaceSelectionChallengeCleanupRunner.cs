using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Auth.Login;

/// <summary>
/// Hard-deletes login_workspace_selection_challenges rows whose expiry or consumption is older
/// than 24 hours, per auth.md. Durable security evidence lives in audit_logs, not this table, so
/// deletion here is safe. Uses ExecuteDeleteAsync (no per-row load/tracking) to avoid broad table
/// locks; safe to run repeatedly if a prior pass was interrupted.
/// </summary>
public sealed class LoginWorkspaceSelectionChallengeCleanupRunner : ILoginWorkspaceSelectionChallengeCleanupRunner
{
    private static readonly TimeSpan RetentionAfterTerminal = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public LoginWorkspaceSelectionChallengeCleanupRunner(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var cutoff = now.Subtract(RetentionAfterTerminal);

        // Implements greatest(expires_at, consumed_at) < cutoff as two explicit branches: a
        // still-unconsumed row has no consumed_at to compare, so only expires_at matters; a
        // consumed row's consumed_at is always >= expires_at at consumption time (ConsumedAt is
        // only ever set after FindActiveRowAsync first confirmed ExpiresAt > now), so comparing
        // consumed_at alone for consumed rows is equivalent to comparing the greatest of the two.
        var deletedRows = await _db.LoginWorkspaceSelectionChallenges
            .Where(c =>
                (c.ConsumedAt != null && c.ConsumedAt <= cutoff) ||
                (c.ConsumedAt == null && c.ExpiresAt <= cutoff))
            .ExecuteDeleteAsync(ct);

        return deletedRows;
    }
}
