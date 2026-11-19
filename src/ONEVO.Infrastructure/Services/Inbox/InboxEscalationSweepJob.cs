using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Inbox;

/// <summary>
/// Hourly SLA sweep for every inbox source with an escalation SLA (plan S2): per active tenant, under that tenant's
/// own context, each <see cref="IInboxEscalationSweepSource"/> escalates its overdue open items. Replaces the 3-day
/// stale sweep that ran inside ExceptionDetectionJob. One source or tenant failing never stops the rest.
/// </summary>
public sealed class InboxEscalationSweepJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<InboxEscalationSweepJob> _logger;

    public InboxEscalationSweepJob(IServiceProvider services, ILogger<InboxEscalationSweepJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "InboxEscalationSweepJob encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var sources = provider.GetServices<IInboxEscalationSweepSource>().ToList();
        if (sources.Count == 0)
            return 0;
        var tenants = provider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = provider.GetRequiredService<ITenantContextSwitcher>();
        var tenantContext = provider.GetRequiredService<IWritableTenantContext>();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        var clock = provider.GetRequiredService<IDateTimeProvider>();

        // The swept tables are under FORCE row-level security: the tenant list needs admin mode, and each
        // tenant's rows need that tenant's own context (mirrors ProjectMonitorJob / ExceptionDetectionJob).
        tenantContext.SetAdminMode();
        var now = clock.UtcNow;
        var escalated = 0;
        foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                foreach (var source in sources)
                {
                    try
                    {
                        escalated += await source.EscalateOverdueAsync(tenant.Id, now, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A failed source's unsaved changes must not be flushed by the next source's save.
                        unitOfWork.ClearTracking();
                        _logger.LogError(ex, "Inbox escalation sweep failed for source {Source} in tenant {TenantId}.", source.Source, tenant.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed tenant's unsaved changes must not be flushed into the next tenant's save.
                unitOfWork.ClearTracking();
                _logger.LogError(ex, "Inbox escalation sweep failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
        if (escalated > 0)
            _logger.LogInformation("Inbox escalation sweep escalated {Count} items.", escalated);
        return escalated;
    }
}
