namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one explicit database transaction: commits after
    /// the operation returns, rolls back if it throws. Use when two or more writes - especially a
    /// raw-SQL write plus tracked-entity SaveChangesAsync - must land atomically together.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
