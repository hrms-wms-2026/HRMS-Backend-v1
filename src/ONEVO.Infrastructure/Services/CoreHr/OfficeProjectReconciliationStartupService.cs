using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.CoreHr;

/// <summary>Runs <see cref="IOfficeProjectReconciler"/> once for every live tenant shortly after
/// startup: the backfill for existing tenants and the recovery path for anything missed. Not a
/// periodic job (SCL-01: background jobs have no multi-instance locking). Skipped in the Test
/// environment so integration-test hosts don't create Office projects behind the tests' backs.</summary>
public sealed class OfficeProjectReconciliationStartupService(
    IServiceProvider services,
    IHostEnvironment environment,
    ILogger<OfficeProjectReconciliationStartupService> logger) : BackgroundService
{
    public const int PageSize = 200;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Test"))
            return;

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before or during the run.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>();

        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(statusFilter: null, searchTerm: null, skip, PageSize, ct);
            foreach (var tenant in page)
            {
                if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                    continue;
                try
                {
                    var outcome = await reconciler.ReconcileAsync(tenant.Id, ct);
                    logger.LogDebug("Office project reconciliation for tenant {TenantId}: {Outcome}.", tenant.Id, outcome);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Office project reconciliation failed for tenant {TenantId}.", tenant.Id);
                }
            }

            if (page.Count < PageSize)
                break;
            skip += PageSize;
        }
    }
}
