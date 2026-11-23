using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>Task 42: <see cref="IRowVersionStore"/> on the request's DbContext, over the xmin rowversion token.</summary>
public sealed class EfRowVersionStore(ApplicationDbContext db) : IRowVersionStore
{
    public string? GetVersion(object entity)
    {
        var entry = db.Entry(entity);
        if (entry.Metadata.FindProperty(RowVersionConfiguration.PropertyName) is null)
            return null;
        return entry.Property(RowVersionConfiguration.PropertyName).CurrentValue is ulong version
            ? RowVersionToken.ToToken(version)
            : null;
    }

    public async Task<string?> GetVersionAsync<TEntity>(Guid id, CancellationToken ct) where TEntity : class
    {
        var version = await db.Set<TEntity>().AsNoTracking()
            .Where(e => EF.Property<Guid>(e, "Id") == id)
            .Select(e => EF.Property<ulong?>(e, RowVersionConfiguration.PropertyName))
            .FirstOrDefaultAsync(ct);
        return version is { } v ? RowVersionToken.ToToken(v) : null;
    }

    public void SetExpectedVersion(object entity, string? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
            return;
        var entry = db.Entry(entity);
        if (entry.State == EntityState.Detached)
            throw new InvalidOperationException("SetExpectedVersion must be called after the entity is tracked (after Update).");
        // An unreadable version can never match, so the save fails as a conflict instead of silently overwriting.
        entry.Property(RowVersionConfiguration.PropertyName).OriginalValue =
            RowVersionToken.TryParse(expectedVersion.Trim(), out var version) ? version : 0UL;
    }
}
