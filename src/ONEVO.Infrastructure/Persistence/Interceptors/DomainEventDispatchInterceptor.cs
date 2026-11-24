using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Domain.Common;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

public class DomainEventDispatchInterceptor : SaveChangesInterceptor
{
    private readonly IPublisher _publisher;

    public DomainEventDispatchInterceptor(IPublisher publisher)
        => _publisher = publisher;

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Improvements task 26: synchronous SaveChanges is not supported. Dispatching the events here
    /// would block a thread-pool thread on async handlers (sync-over-async), which under load
    /// starves the pool. Nothing in the app calls it; this keeps it that way.
    /// </summary>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
        throw SyncDatabaseCalls.NotSupported("SaveChanges", "SaveChangesAsync");

    // Refused before anything is written, so a synchronous call cannot leave saved rows behind
    // with their domain events never published.
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        throw SyncDatabaseCalls.NotSupported("SaveChanges", "SaveChangesAsync");

    private async Task DispatchDomainEventsAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null) return;

        var entities = context.ChangeTracker
            .Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entities.SelectMany(e => e.DomainEvents).ToList();

        entities.ForEach(e => e.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
            await _publisher.Publish(domainEvent, ct);
    }
}
