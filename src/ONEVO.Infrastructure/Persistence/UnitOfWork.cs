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
        if (_db.Database.CurrentTransaction is not null)
            return await operation(ct);

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public void ClearTracking() => _db.ChangeTracker.Clear();
}
