using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// Owner guards of OutboxClaimStore, executed as onevo_app (NOBYPASSRLS): only the instance that
/// currently holds a message's claim can renew, complete, fail or release it, and a still-valid
/// lease is never taken by another instance. Requires Docker.
/// </summary>
public sealed class OutboxClaimOwnershipIntegrationTests
{
    private const string Type = "outbox_it_ownership";
    private static readonly TimeSpan LongLease = TimeSpan.FromMinutes(5);

    private static async Task<T> WithAppDbAsync<T>(IServiceProvider services, Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static OutboxFailureOutcome RetryOutcome() =>
        OutboxRetryPolicy.OnFailure(0, new InvalidOperationException("late failure"), DateTimeOffset.UtcNow);

    [Fact]
    public async Task NonOwner_CannotRenewCompleteFailOrRelease_AndTheOwnerStillCan()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(Type);
        var services = host.BuildServices(_ => { });
        var claimed = await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-a", LongLease, 20, CancellationToken.None));
        Assert.Equal(id, Assert.Single(claimed).Id);

        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.RenewLeaseAsync(db, id, "intruder-b", LongLease, CancellationToken.None)));
        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.CompleteAsync(db, id, "intruder-b", DateTimeOffset.UtcNow, CancellationToken.None)));
        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.FailAsync(db, id, "intruder-b", RetryOutcome(), CancellationToken.None)));
        Assert.Equal(0, await WithAppDbAsync(services, db => OutboxClaimStore.ReleaseAsync(db, [id], "intruder-b", CancellationToken.None)));

        var untouched = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Processing, untouched.Status);
        Assert.Equal("owner-a", untouched.LockedBy);
        Assert.Equal(0, untouched.AttemptCount);

        Assert.True(await WithAppDbAsync(services, db => OutboxClaimStore.CompleteAsync(db, id, "owner-a", DateTimeOffset.UtcNow, CancellationToken.None)));
        var published = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Published, published.Status);
        Assert.Null(published.LockedBy);
        Assert.Null(published.LockedUntil);
    }

    [Fact]
    public async Task AnExpiredClaimWhoseHandlerNeverStarted_IsReclaimedWithoutCountingAnAttempt()
    {
        // One instance claims a batch of two but only starts the first (renewing its lease right before the handler);
        // it stalls on that one, and both leases expire. The second message never ran, so its reclaim must not cost it
        // an attempt - otherwise an outage that slows the first sends parks the rest of the batch without a single try.
        var host = await OutboxTestHost.CreateAsync();
        var started = await host.SeedPendingAsync(Type);
        var waiting = await host.SeedPendingAsync(Type);
        var services = host.BuildServices(_ => { });
        var shortLease = TimeSpan.FromSeconds(1);
        var claimed = await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-a", shortLease, 20, CancellationToken.None));
        Assert.Equal(2, claimed.Count);
        Assert.True(await WithAppDbAsync(services, db => OutboxClaimStore.RenewLeaseAsync(db, started, "owner-a", shortLease, CancellationToken.None)));

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var reclaimed = await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-b", LongLease, 20, CancellationToken.None));

        Assert.Equal(2, reclaimed.Count);
        Assert.Equal(1, (await host.GetAsync(started)).AttemptCount);
        var neverRan = await host.GetAsync(waiting);
        Assert.Equal(0, neverRan.AttemptCount);
        Assert.Null(neverRan.LastError);
    }

    [Fact]
    public async Task AValidLease_IsNeverClaimedByAnotherInstance()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(Type);
        var services = host.BuildServices(_ => { });

        var first = await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-a", LongLease, 20, CancellationToken.None));
        var second = await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-b", LongLease, 20, CancellationToken.None));

        Assert.Equal(id, Assert.Single(first).Id);
        Assert.Empty(second);
        Assert.Equal("owner-a", (await host.GetAsync(id)).LockedBy);
    }

    [Fact]
    public async Task ExpiredLeaseHolder_CannotCompleteOrFail_OnceAnotherInstanceReclaimedTheMessage()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(Type);
        var services = host.BuildServices(_ => { });
        await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-a", TimeSpan.FromSeconds(1), 20, CancellationToken.None));
        // owner-a starts the handler (the renewal right before it), then stalls past its lease.
        await WithAppDbAsync(services, db => OutboxClaimStore.RenewLeaseAsync(db, id, "owner-a", TimeSpan.FromSeconds(1), CancellationToken.None));

        List<OutboxMessage> reclaimed = [];
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (reclaimed.Count == 0)
            {
                await Task.Delay(250, timeout.Token);
                reclaimed = await WithAppDbAsync(services, db => OutboxClaimStore.ClaimBatchAsync(db, "owner-b", LongLease, 20, CancellationToken.None));
            }
        }

        Assert.Equal(id, Assert.Single(reclaimed).Id);
        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.CompleteAsync(db, id, "owner-a", DateTimeOffset.UtcNow, CancellationToken.None)));
        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.FailAsync(db, id, "owner-a", RetryOutcome(), CancellationToken.None)));
        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.RenewLeaseAsync(db, id, "owner-a", LongLease, CancellationToken.None)));

        var held = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Processing, held.Status);
        Assert.Equal("owner-b", held.LockedBy);
        Assert.Equal(1, held.AttemptCount);

        // After the new owner publishes, the old owner still cannot overwrite the outcome.
        Assert.True(await WithAppDbAsync(services, db => OutboxClaimStore.CompleteAsync(db, id, "owner-b", DateTimeOffset.UtcNow, CancellationToken.None)));
        Assert.False(await WithAppDbAsync(services, db => OutboxClaimStore.FailAsync(db, id, "owner-a", RetryOutcome(), CancellationToken.None)));
        Assert.Equal(OutboxMessageStatus.Published, (await host.GetAsync(id)).Status);
    }
}
