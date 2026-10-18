using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// Polls the outbox and dispatches due messages to their registered IOutboxMessageHandler.
/// Failures are retried with exponential backoff; after MaxAttempts the message is parked as
/// failed. Also purges expired idempotency records once per hour.
///
/// Every API instance runs this processor (AddHostedService), so messages are claimed atomically in
/// the database before being dispatched (OutboxClaimStore): a claimed message belongs to this
/// instance (InstanceId) until its lease expires, and only the owner can complete or fail it.
///
/// Delivery guarantee: at-least-once with no concurrent double-processing. It is NOT exactly-once.
/// Claiming stops competing processors from sending the same message at the same time. A crash
/// after the email provider accepted a message but before the row is marked Published still causes
/// a retry once the lease expires, which may deliver a duplicate. The retry reuses the same
/// provider idempotency key (OutboxDispatchContext), which Resend honours for 24 hours; SendGrid
/// has no idempotency mechanism, so with SendGrid that duplicate is possible.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly TimeSpan _pollInterval;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public OutboxProcessor(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<OutboxProcessor> logger)
    {
        _services = services;
        _logger = logger;
        var pollSeconds = configuration.GetValue("Outbox:PollSeconds", 10);
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, pollSeconds));
        LeaseSettings = OutboxLeaseSettings.FromConfiguration(configuration);
        InstanceId = NewInstanceId();
    }

    /// <summary>Claim owner id (outbox_messages.locked_by), unique per processor instance.</summary>
    public string InstanceId { get; }

    public OutboxLeaseSettings LeaseSettings { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await PurgeIdempotencyRecordsIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must survive transient failures (e.g. DB briefly down).
                _logger.LogWarning(ex, "Outbox processing iteration failed; will retry.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Claims one batch and dispatches it. Returns the number of messages claimed.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var writableTenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var dispatchContext = scope.ServiceProvider.GetRequiredService<OutboxDispatchContext>();
        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
            .ToDictionary(h => h.Type, StringComparer.Ordinal);

        // The scope's connection starts in system mode, as the unclaimed SELECT used to.
        var claimed = await OutboxClaimStore.ClaimBatchAsync(db, InstanceId, LeaseSettings.Lease, BatchSize, ct);
        if (claimed.Count == 0)
            return 0;

        var notStarted = new Queue<OutboxMessage>(claimed);
        try
        {
            // A message stays in notStarted (and is released by the finally below on shutdown)
            // until DispatchAsync takes it over; from then on DispatchAsync handles its release.
            while (notStarted.TryPeek(out var message))
            {
                ct.ThrowIfCancellationRequested();

                if (OutboxRetryPolicy.ShouldPark(message.AttemptCount))
                {
                    // Reclaimed after its attempts kept dying with their instance (lease expired
                    // every time): stop here, exactly as MaxAttempts ordinary failures would.
                    notStarted.Dequeue();
                    await ParkAsync(db, clock, message);
                    continue;
                }

                // This scope's DbContext/connection starts in system mode (no RLS tenant set), which
                // is correct for the platform-scoped outbox table but wrong for any tenant-scoped row
                // a handler writes (e.g. Notification) - RLS rejects it outright. Switch to the
                // message's own tenant before invoking its handler, same mechanism request-time
                // middleware and LeaveYearEndEntitlementJob use.
                if (message.TenantId is { } tenantId)
                {
                    var tenant = await tenantRepository.GetByIdAsync(tenantId, ct);
                    if (tenant is not null)
                        await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                }
                else
                {
                    writableTenantContext.SetSystemMode();
                }

                notStarted.Dequeue();
                await DispatchAsync(db, encryption, clock, dispatchContext, handlers, message, ct);
            }
        }
        finally
        {
            // Shutdown (or an unexpected error) before these messages started: hand them back now
            // rather than leaving them to wait for lease expiry.
            if (notStarted.Count > 0)
                await ReleaseQuietlyAsync(db, notStarted.Select(m => m.Id).ToList());
        }

        return claimed.Count;
    }

    private async Task DispatchAsync(
        ApplicationDbContext db,
        IEncryptionService encryption,
        IDateTimeProvider clock,
        OutboxDispatchContext dispatchContext,
        IReadOnlyDictionary<string, IOutboxMessageHandler> handlers,
        OutboxMessage message,
        CancellationToken ct)
    {
        // Not cancellable: a shutdown observed here would leave the message claimed until its
        // lease expires; the handler below observes the stopping token and releases it instead.
        if (!await OutboxClaimStore.RenewLeaseAsync(db, message.Id, InstanceId, LeaseSettings.Lease, CancellationToken.None))
        {
            // Our lease ran out while earlier messages of the batch were dispatched, and another
            // instance has reclaimed this one. It is theirs now.
            _logger.LogInformation(
                "Outbox message {MessageId} ({Type}) was reclaimed by another instance before {InstanceId} started it; skipping.",
                message.Id, message.Type, InstanceId);
            return;
        }

        Exception? failure = null;
        using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            // Stop the handler while its lease is still valid, so it can never still be running
            // when another instance reclaims the message.
            attempt.CancelAfter(LeaseSettings.HandlerTimeout);
            dispatchContext.Begin(message.Id);
            try
            {
                if (!handlers.TryGetValue(message.Type, out var handler))
                    throw new InvalidOperationException($"No outbox handler registered for type '{message.Type}'.");

                var payloadJson = encryption.Decrypt(message.EncryptedPayload);
                await handler.HandleAsync(payloadJson, attempt.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutting down mid-handler: discard its staged writes and hand the message back
                // without counting an attempt. If the handler had already sent, this is the same
                // crash-after-send case as any other: the next owner retries it (at-least-once).
                db.ChangeTracker.Clear();
                await ReleaseQuietlyAsync(db, [message.Id]);
                throw;
            }
            catch (OperationCanceledException ex) when (attempt.IsCancellationRequested)
            {
                failure = new TimeoutException(
                    $"Outbox handler for '{message.Type}' exceeded its {LeaseSettings.HandlerTimeout.TotalSeconds:0.#} s time budget and was cancelled.",
                    ex);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                dispatchContext.End();
            }
        }

        // Outcome writes deliberately ignore the stopping token: once a handler has run, losing
        // its result to shutdown timing would only cause an avoidable retry.
        await RecordOutcomeAsync(db, clock, message, failure);
    }

    private async Task RecordOutcomeAsync(
        ApplicationDbContext db, IDateTimeProvider clock, OutboxMessage message, Exception? failure)
    {
        if (failure is null)
        {
            try
            {
                await SaveWithOutcomeAsync(db, message,
                    () => OutboxClaimStore.CompleteAsync(db, message.Id, InstanceId, clock.UtcNow, CancellationToken.None));
                return;
            }
            catch (Exception ex)
            {
                // The handler succeeded but the rows it staged could not be saved: the attempt failed.
                db.ChangeTracker.Clear();
                failure = ex;
            }
        }

        var outcome = OutboxRetryPolicy.OnFailure(message.AttemptCount, failure, clock.UtcNow);
        bool owned;
        try
        {
            // As before claiming: whatever the handler staged before it threw is saved together
            // with the failure (e.g. CalendarIncidentEvaluateOutboxHandler keeps the employees it
            // did evaluate).
            owned = await SaveWithOutcomeAsync(db, message,
                () => OutboxClaimStore.FailAsync(db, message.Id, InstanceId, outcome, CancellationToken.None));
        }
        catch (Exception saveError)
        {
            _logger.LogWarning(saveError,
                "Outbox message {MessageId} ({Type}): the failed attempt's staged changes could not be saved; recording the failure without them.",
                message.Id, message.Type);
            db.ChangeTracker.Clear();
            owned = await SaveWithOutcomeAsync(db, message,
                () => OutboxClaimStore.FailAsync(db, message.Id, InstanceId, outcome, CancellationToken.None));
        }

        if (!owned)
            return;

        if (outcome.Status == OutboxMessageStatus.Failed)
            _logger.LogError(failure,
                "Outbox message {MessageId} ({Type}) failed permanently after {Attempts} attempts.",
                message.Id, message.Type, outcome.AttemptCount);
        else
            _logger.LogWarning(failure,
                "Outbox message {MessageId} ({Type}) attempt {Attempt} failed; next attempt at {NextAttempt}.",
                message.Id, message.Type, outcome.AttemptCount, outcome.NextAttemptAt);
    }

    /// <summary>
    /// Saves what the handler staged on the shared DbContext (e.g. a Notification row) and applies
    /// the owner-guarded state change in one transaction, so both land or neither does. When this
    /// instance no longer owns the message the transaction is rolled back: the new owner will run
    /// the handler again and its result wins. Returns whether this instance still owned it.
    /// </summary>
    private async Task<bool> SaveWithOutcomeAsync(ApplicationDbContext db, OutboxMessage message, Func<Task<bool>> guardedUpdate)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        if (!await guardedUpdate())
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            _logger.LogWarning(
                "Outbox message {MessageId} ({Type}) is no longer owned by {InstanceId} (its lease expired and another instance reclaimed it); this attempt's result was discarded.",
                message.Id, message.Type, InstanceId);
            return false;
        }

        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task ParkAsync(ApplicationDbContext db, IDateTimeProvider clock, OutboxMessage message)
    {
        var outcome = OutboxRetryPolicy.Park(message.AttemptCount, message.LastError ?? "Attempts did not finish before their lease expired.", clock.UtcNow);
        if (await OutboxClaimStore.FailAsync(db, message.Id, InstanceId, outcome, CancellationToken.None))
            _logger.LogError(
                "Outbox message {MessageId} ({Type}) failed permanently after {Attempts} attempts that did not finish before their lease expired. Last error: {LastError}",
                message.Id, message.Type, outcome.AttemptCount, outcome.LastError);
    }

    private async Task ReleaseQuietlyAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> ids)
    {
        try
        {
            await OutboxClaimStore.ReleaseAsync(db, ids, InstanceId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Not fatal: the claims expire with their lease and are reclaimed then.
            _logger.LogWarning(ex, "Could not release {Count} claimed outbox message(s); they will be reclaimed after lease expiry.", ids.Count);
        }
    }

    private async Task PurgeIdempotencyRecordsIfDueAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastPurge < PurgeInterval)
            return;
        _lastPurge = DateTimeOffset.UtcNow;

        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var purged = await store.PurgeExpiredAsync(ct);
        if (purged > 0)
            _logger.LogInformation("Purged {Count} expired idempotency records.", purged);
    }

    private static string NewInstanceId()
    {
        var id = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}";
        return id.Length <= OutboxMessage.LockedByMaxLength ? id : id[^OutboxMessage.LockedByMaxLength..];
    }
}
