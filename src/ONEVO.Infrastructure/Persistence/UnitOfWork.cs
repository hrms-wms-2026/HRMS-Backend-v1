using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _db;

    public UnitOfWork(ApplicationDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken ct = default)
    {
        // Re-entrant: inside an open transaction (e.g. the Inbox wrapping a module command that runs its own
        // transaction, so the command and its journal row commit together) the operation joins that transaction.
        if (_db.Database.CurrentTransaction is { } outer)
            return await JoinWithSavepointAsync(outer, operation, ct);

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    /// <summary>
    /// A nested operation keeps its own all-or-nothing promise: when it throws, its writes are rolled
    /// back to a savepoint before the exception leaves. A caller that catches the exception and
    /// returns a failure (ApproveAccessGrantRequest's reserved-seat check, say) must not have the
    /// half-done writes committed by the outer transaction (AuditingBehavior, the Inbox).
    /// </summary>
    private static async Task<TResult> JoinWithSavepointAsync<TResult>(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction outer,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken ct)
    {
        if (!outer.SupportsSavepoints)
            return await operation(ct);

        // SQL Server savepoint names are at most 32 characters.
        var savepoint = "uow_" + Guid.NewGuid().ToString("N")[..20];
        await outer.CreateSavepointAsync(savepoint, ct);
        try
        {
            var result = await operation(ct);
            await outer.ReleaseSavepointAsync(savepoint, ct);
            return result;
        }
        catch
        {
            try
            {
                await outer.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            catch
            {
                // The outer transaction is already doomed (a deadlock victim, say): it rolls back as
                // a whole, so the original exception is the one worth surfacing.
            }

            throw;
        }
    }

    public void ClearTracking() => _db.ChangeTracker.Clear();
}
