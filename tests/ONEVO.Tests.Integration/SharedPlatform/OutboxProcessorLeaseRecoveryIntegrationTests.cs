using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// Crash recovery through lease expiry, with the real email pipeline in front of a fake provider.
/// Delivery guarantee under test: at-least-once with no concurrent double-processing - never
/// exactly-once. A crash before the send loses nothing and sends once; a crash after the provider
/// accepted the send but before the row was marked Published is retried. With Resend the retry
/// carries the same Idempotency-Key, so Resend returns the original response instead of sending
/// again; SendGrid has no idempotency mechanism, so there the retry is a real duplicate.
/// Requires Docker.
/// </summary>
public sealed class OutboxProcessorLeaseRecoveryIntegrationTests
{
    private static readonly TimeSpan ShortLease = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);

    private static Task<Guid> SeedPasswordResetAsync(OutboxTestHost host) =>
        host.SeedPendingAsync(OutboxMessageTypes.PasswordResetEmail, host.TenantId,
            new PasswordResetEmailPayload(host.TenantId, Guid.NewGuid(), "reset-me@outbox.test", "raw-reset-token-1", "outbox-tenant"));

    [Fact]
    public async Task CrashBeforeSend_IsReclaimedOnlyAfterTheLeaseExpires_AndSentExactlyOnce()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await SeedPasswordResetAsync(host);
        var provider = new FakeEmailProvider("resend");
        var services = host.BuildServices(provider.Register);

        // An instance claims the message and dies before sending anything.
        var claimed = await host.ClaimAsCrashedInstanceAsync(services, "crashed-instance", ShortLease);
        Assert.Equal(id, Assert.Single(claimed).Id);

        // While that lease is valid no other instance may touch it.
        var healthy = OutboxTestHost.CreateProcessor(services);
        Assert.Equal(0, await OutboxTestHost.DrainAsync(healthy, CancellationToken.None));
        Assert.Empty(provider.Accepted);
        var held = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Processing, held.Status);
        Assert.Equal("crashed-instance", held.LockedBy);

        // Once it expires the healthy instance reclaims and sends it.
        await OutboxTestHost.DrainUntilAsync(healthy, () => provider.Accepted.Count >= 1, RecoveryTimeout);
        await OutboxTestHost.DrainAsync(healthy, CancellationToken.None);

        Assert.Single(provider.Accepted);
        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Published, row.Status);
        // The crashed instance's abandoned attempt is counted towards MaxAttempts.
        Assert.Equal(1, row.AttemptCount);
        Assert.Null(row.LastError);
        Assert.Null(row.LockedBy);
        Assert.Null(row.LockedUntil);
        Assert.NotNull(row.AttemptStartedAt);
    }

    [Fact]
    public async Task CrashAfterResendAccepted_IsRetriedAfterLeaseExpiry_WithTheSameIdempotencyKeyAndPayload()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await SeedPasswordResetAsync(host);
        var provider = new FakeEmailProvider("resend");
        var services = host.BuildServices(provider.Register);

        // A real processor sends; the provider accepts; the instance then freezes and never
        // acknowledges - from the database's point of view, a crash after provider acceptance.
        var frozen = new TaskCompletionSource();
        provider.AfterAccept = call => call == 1 ? frozen.Task : Task.CompletedTask;
        var crashed = OutboxTestHost.CreateProcessor(services, leaseSeconds: (int)ShortLease.TotalSeconds);
        var crashedRun = Task.Run(() => crashed.ProcessBatchAsync(CancellationToken.None));
        try
        {
            await WaitForAsync(() => provider.Accepted.Count == 1);
            Assert.Equal(OutboxMessageStatus.Processing, (await host.GetAsync(id)).Status);

            var healthy = OutboxTestHost.CreateProcessor(services);
            await OutboxTestHost.DrainUntilAsync(healthy, () => provider.Accepted.Count >= 2, RecoveryTimeout);

            var first = provider.Accepted[0];
            var retry = provider.Accepted[1];
            Assert.Equal($"outbox/{id:N}/1", first.IdempotencyKey);
            Assert.Equal(first.IdempotencyKey, retry.IdempotencyKey);
            // Same key AND same payload is what makes Resend return the original response instead
            // of sending again (a different payload under the key would be a 409).
            Assert.Equal(first.Body, retry.Body);
            Assert.Equal(OutboxMessageStatus.Published, (await host.GetAsync(id)).Status);
        }
        finally
        {
            frozen.TrySetResult();
            await crashedRun;
        }
    }

    [Fact]
    public async Task CrashAfterSendGridAccepted_IsRetriedAfterLeaseExpiry_WithoutAKey_SoADuplicateIsPossible()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await SeedPasswordResetAsync(host);
        var provider = new FakeEmailProvider("sendgrid");
        var services = host.BuildServices(provider.Register);

        var frozen = new TaskCompletionSource();
        provider.AfterAccept = call => call == 1 ? frozen.Task : Task.CompletedTask;
        var crashed = OutboxTestHost.CreateProcessor(services, leaseSeconds: (int)ShortLease.TotalSeconds);
        var crashedRun = Task.Run(() => crashed.ProcessBatchAsync(CancellationToken.None));
        try
        {
            await WaitForAsync(() => provider.Accepted.Count == 1);
            var healthy = OutboxTestHost.CreateProcessor(services);
            await OutboxTestHost.DrainUntilAsync(healthy, () => provider.Accepted.Count >= 2, RecoveryTimeout);

            // SendGrid documents no idempotency key: both sends reach it with nothing to
            // deduplicate on, so the recipient may get the email twice (at-least-once).
            Assert.Equal(2, provider.Accepted.Count);
            Assert.All(provider.Accepted, send => Assert.Null(send.IdempotencyKey));
            Assert.Equal(OutboxMessageStatus.Published, (await host.GetAsync(id)).Status);
        }
        finally
        {
            frozen.TrySetResult();
            await crashedRun;
        }
    }

    [Fact]
    public async Task InstanceWhoseLeaseExpired_CannotMarkPublished_AMessageAnotherInstanceReclaimedAndPublished()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await SeedPasswordResetAsync(host);
        var provider = new FakeEmailProvider("resend");
        var services = host.BuildServices(provider.Register);

        var frozen = new TaskCompletionSource();
        provider.AfterAccept = call => call == 1 ? frozen.Task : Task.CompletedTask;
        var stale = OutboxTestHost.CreateProcessor(services, leaseSeconds: (int)ShortLease.TotalSeconds);
        var staleRun = Task.Run(() => stale.ProcessBatchAsync(CancellationToken.None));

        await WaitForAsync(() => provider.Accepted.Count == 1);
        var healthy = OutboxTestHost.CreateProcessor(services);
        await OutboxTestHost.DrainUntilAsync(healthy, () => provider.Accepted.Count >= 2, RecoveryTimeout);
        var publishedByNewOwner = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Published, publishedByNewOwner.Status);

        // The stale instance wakes up and tries to record its own (late) result.
        frozen.TrySetResult();
        await staleRun;

        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Published, row.Status);
        Assert.Equal(publishedByNewOwner.ProcessedAt, row.ProcessedAt);
        Assert.Equal(publishedByNewOwner.AttemptCount, row.AttemptCount);
        Assert.Null(row.LastError);
        Assert.Null(row.LockedBy);
        Assert.Equal(2, provider.Accepted.Count);
    }

    [Fact]
    public async Task MessageWhoseAttemptsKeepDyingWithTheirInstance_IsParkedAsFailedAtMaxAttempts_WithoutAnotherSend()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await SeedPasswordResetAsync(host);
        var provider = new FakeEmailProvider("resend");
        var services = host.BuildServices(provider.Register);
        // Seven attempts already counted; the eighth instance to take it dies too.
        await using (var db = host.NewAdminContext())
        {
            var message = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == id);
            message.AttemptCount = 7;
            await db.SaveChangesAsync();
        }
        await host.ClaimAsCrashedInstanceAsync(services, "crashed-eighth", ShortLease);

        var healthy = OutboxTestHost.CreateProcessor(services);
        await OutboxTestHost.DrainUntilAsync(healthy,
            () => host.GetAsync(id).GetAwaiter().GetResult().Status == OutboxMessageStatus.Failed, RecoveryTimeout);

        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Failed, row.Status);
        Assert.Equal(8, row.AttemptCount);
        Assert.Contains("crashed-eighth", row.LastError);
        Assert.Contains("lease expired", row.LastError);
        Assert.NotNull(row.ProcessedAt);
        Assert.Null(row.LockedBy);
        Assert.Empty(provider.Accepted);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(RecoveryTimeout);
        while (!condition())
            await Task.Delay(50, cts.Token);
    }
}
