using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.WorkManagement;

/// <summary>
/// Hourly project monitor: evaluates every active project's Modules, sprints and tasks (capacity,
/// deadline overload, overdue, clocked hours), stores the open problems in wm_monitor_alerts and
/// notifies each new problem's creator position once. Replaces SprintLifecycleJob - an overdue
/// sprint is now one of the monitor rules. Never changes any Module, sprint or task.
/// </summary>
public sealed class ProjectMonitorJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<ProjectMonitorJob> _logger;

    public ProjectMonitorJob(IServiceProvider services, ILogger<ProjectMonitorJob> logger)
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
                    _logger.LogError(ex, "ProjectMonitorJob encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // PeriodicTimer.WaitForNextTickAsync throws when its token is cancelled; an ordinary host
            // shutdown must not surface as an unhandled exception (StopHost treats that as a crash).
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var projects = provider.GetRequiredService<IProjectRepository>();
        var monitor = provider.GetRequiredService<IProjectMonitorService>();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var tenantContext = provider.GetRequiredService<IWritableTenantContext>();
        var tenants = provider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = provider.GetRequiredService<ITenantContextSwitcher>();

        // WM tables are under FORCE row-level security: the cross-tenant project list needs admin
        // mode, and each tenant's rows need that tenant's context. Mirrors WellnessRuleEvaluatorJob.
        tenantContext.SetAdminMode();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var activeProjects = await projects.ListActiveAcrossTenantsAsync(ct);

        foreach (var tenantGroup in activeProjects.GroupBy(p => p.TenantId))
        {
            ct.ThrowIfCancellationRequested();
            var tenant = await tenants.GetByIdAsync(tenantGroup.Key, ct);
            if (tenant is null)
                continue;

            try
            {
                await tenantSwitcher.SwitchToTenantAsync(
                    new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

                var opened = 0;
                foreach (var project in tenantGroup)
                    opened += await monitor.EvaluateProjectAsync(tenant.Id, project.Id, today, ct);

                await db.SaveChangesAsync(ct);
                if (opened > 0)
                    _logger.LogInformation("ProjectMonitorJob opened {Count} alerts for tenant {TenantId}.", opened, tenant.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's bad data must not stop the sweep for the others.
                db.ChangeTracker.Clear();
                _logger.LogError(ex, "ProjectMonitorJob failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }
}
