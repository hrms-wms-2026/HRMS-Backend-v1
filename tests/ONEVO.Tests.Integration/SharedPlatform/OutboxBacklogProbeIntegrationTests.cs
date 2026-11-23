using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Observability;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>Task 17: the outbox backlog query, as the app login under row-level security, sees every tenant's rows.</summary>
public sealed class OutboxBacklogProbeIntegrationTests
{
    [Fact]
    public async Task CountsPendingAndFailed_AcrossTenants_WithTheOldestPendingAge()
    {
        var host = await OutboxTestHost.CreateAsync();
        await host.SeedPendingAsync("probe.test", host.TenantId);
        await host.SeedPendingAsync("probe.test", tenantId: null);
        var failedId = await host.SeedPendingAsync("probe.test", host.TenantId);
        await using (var admin = host.NewAdminContext())
        {
            var failed = await admin.Set<OutboxMessage>().FindAsync(failedId);
            failed!.Status = OutboxMessageStatus.Failed;
            await admin.SaveChangesAsync();
        }

        var services = host.BuildServices(_ => { });
        var probe = new OutboxBacklogProbe(services.GetRequiredService<IServiceScopeFactory>());

        var backlog = await probe.GetAsync(CancellationToken.None);

        Assert.Equal(2, backlog.Pending);
        Assert.Equal(1, backlog.Failed);
        Assert.NotNull(backlog.OldestPendingAge);
        Assert.True(backlog.OldestPendingAge >= TimeSpan.Zero);
    }

    [Fact]
    public async Task EmptyOutbox_HasNoOldestPendingAge()
    {
        var host = await OutboxTestHost.CreateAsync();
        var services = host.BuildServices(_ => { });

        var backlog = await new OutboxBacklogProbe(services.GetRequiredService<IServiceScopeFactory>())
            .GetAsync(CancellationToken.None);

        Assert.Equal(0, backlog.Pending);
        Assert.Null(backlog.OldestPendingAge);
    }
}
