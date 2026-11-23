namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Task 42: the optimistic-concurrency version of a row that has a rowversion token, as the string the client holds.
/// Update handlers return it, the client sends it back with its next save, and a stale one makes the save fail with
/// 409 (ConcurrencyConflict). Sending no version keeps the old behaviour (the last save wins).
/// </summary>
public interface IRowVersionStore
{
    /// <summary>The current version of a tracked entity (after its save, the new one). Null when it has no token.</summary>
    string? GetVersion(object entity);

    /// <summary>The stored version of the row with this id, for detail responses. Null when there is no such row.</summary>
    Task<string?> GetVersionAsync<TEntity>(Guid id, CancellationToken ct) where TEntity : class;

    /// <summary>Makes the next save of <paramref name="entity"/> succeed only if the row still has
    /// <paramref name="expectedVersion"/>. Call after the repository's Update. No-op when the version is null or empty;
    /// a version that cannot be read counts as stale.</summary>
    void SetExpectedVersion(object entity, string? expectedVersion);
}
