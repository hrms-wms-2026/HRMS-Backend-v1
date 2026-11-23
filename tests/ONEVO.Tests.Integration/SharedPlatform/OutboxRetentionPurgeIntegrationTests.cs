using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// Task 31: the purge, as the app login under row-level security, deletes old published messages of every tenant
/// (and tenant-less ones) and nothing else.
/// </summary>
public sealed class OutboxRetentionPurgeIntegrationTests
{
    [Fact]
    public async Task DeletesOnlyPublishedMessagesOlderThanTheRetention()
    {
        var host = await OutboxTestHost.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var oldTenantPublished = await SeedAsync(host, OutboxMessageStatus.Published, now.AddDays(-20), host.TenantId);
        var oldPlatformPublished = await SeedAsync(host, OutboxMessageStatus.Published, now.AddDays(-15), tenantId: null);
        var recentPublished = await SeedAsync(host, OutboxMessageStatus.Published, now.AddDays(-2), host.TenantId);
        var oldFailed = await SeedAsync(host, OutboxMessageStatus.Failed, now.AddDays(-30), host.TenantId);
        var oldPending = await SeedAsync(host, OutboxMessageStatus.Pending, processedAt: null, host.TenantId);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OutboxRetentionPurgeJob.RetentionDaysKey] = "14" })
            .Build();
        var job = new OutboxRetentionPurgeJob(host.BuildServices(_ => { }), configuration, NullLogger<OutboxRetentionPurgeJob>.Instance);

        var deleted = await job.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        var remaining = (await host.GetAllAsync()).Select(m => m.Id).ToHashSet();
        Assert.DoesNotContain(oldTenantPublished, remaining);
        Assert.DoesNotContain(oldPlatformPublished, remaining);
        Assert.Contains(recentPublished, remaining);
        Assert.Contains(oldFailed, remaining);
        Assert.Contains(oldPending, remaining);
    }

    [Fact]
    public async Task NothingToPurge_ReturnsZero()
    {
        var host = await OutboxTestHost.CreateAsync();
        await SeedAsync(host, OutboxMessageStatus.Published, DateTimeOffset.UtcNow.AddDays(-1), host.TenantId);
        var job = new OutboxRetentionPurgeJob(host.BuildServices(_ => { }),
            new ConfigurationBuilder().Build(), NullLogger<OutboxRetentionPurgeJob>.Instance);

        Assert.Equal(0, await job.RunOnceAsync(CancellationToken.None));
    }

    private static async Task<Guid> SeedAsync(OutboxTestHost host, string status, DateTimeOffset? processedAt, Guid? tenantId)
    {
        var id = await host.SeedPendingAsync("purge.test", tenantId);
        await using var admin = host.NewAdminContext();
        var message = await admin.Set<OutboxMessage>().FindAsync(id);
        message!.Status = status;
        message.ProcessedAt = processedAt;
        await admin.SaveChangesAsync();
        return id;
    }
}
