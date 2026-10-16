using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.TimeAttendance;

/// <summary>
/// Hourly: per active tenant, under that tenant's own context, materialises the attendance alerts of every active
/// employee (plan E1, <see cref="IAttendanceAlertMaterializer"/>). One employee or tenant failing never stops the rest.
/// </summary>
public sealed class AttendanceAlertMaterializerJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    private readonly IServiceProvider _services;
    private readonly ILogger<AttendanceAlertMaterializerJob> _logger;

    public AttendanceAlertMaterializerJob(IServiceProvider services, ILogger<AttendanceAlertMaterializerJob> logger)
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
                    _logger.LogError(ex, "AttendanceAlertMaterializerJob encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    /// <summary>Returns the number of alert rows added plus removed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var tenants = provider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = provider.GetRequiredService<ITenantContextSwitcher>();
        var tenantContext = provider.GetRequiredService<IWritableTenantContext>();
        var employees = provider.GetRequiredService<IEmployeeRepository>();
        var materializer = provider.GetRequiredService<IAttendanceAlertMaterializer>();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();

        // attendance_alerts is under FORCE row-level security: the tenant list needs admin mode, and each tenant's
        // rows need that tenant's own context (mirrors InboxEscalationSweepJob).
        tenantContext.SetAdminMode();
        var changed = 0;
        foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                foreach (var employee in await employees.ListActiveByLegalEntityAsync(tenant.Id, null, ct))
                {
                    try
                    {
                        var result = await materializer.MaterializeAsync(tenant.Id, employee.Id, employee.LegalEntityId, ct);
                        changed += result.Added + result.Removed;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A failed employee's unsaved changes must not be flushed by the next employee's save.
                        unitOfWork.ClearTracking();
                        _logger.LogError(ex, "Attendance alert materialisation failed for employee {EmployeeId} in tenant {TenantId}.", employee.Id, tenant.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed tenant's unsaved changes must not be flushed into the next tenant's save.
                unitOfWork.ClearTracking();
                _logger.LogError(ex, "Attendance alert materialisation failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
        if (changed > 0)
            _logger.LogInformation("Attendance alert materialisation changed {Count} alerts.", changed);
        return changed;
    }
}
