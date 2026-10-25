using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Leave;

/// <summary>Runs leave crediting once per UTC day after 01:00. Tenant switching is explicit so
/// every tenant is processed under its own RLS context and saved independently.</summary>
public sealed class LeaveCreditJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeOnly TargetUtcTime = new(1, 0);
    private readonly IServiceProvider _services;
    private readonly ILogger<LeaveCreditJob> _logger;
    private DateOnly? _lastProcessedDate;

    public LeaveCreditJob(IServiceProvider services, ILogger<LeaveCreditJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                if (TimeOnly.FromDateTime(now.UtcDateTime) >= TargetUtcTime && _lastProcessedDate != today)
                {
                    await RunAsync(today, stoppingToken);
                    _lastProcessedDate = today;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leave credit job iteration failed; will retry.");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RunAsync(DateOnly today, CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        tenantContext.SetAdminMode();
        var tenants = await scope.ServiceProvider.GetRequiredService<ITenantRepository>()
            .ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct);

        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            try { await ProcessTenantAsync(scope.ServiceProvider, tenant, today, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leave crediting failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
    }

    private static async Task ProcessTenantAsync(
        IServiceProvider services, Tenant tenant, DateOnly today, CancellationToken ct)
    {
        await services.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
        var employees = await services.GetRequiredService<IEmployeeRepository>()
            .ListActiveByLegalEntityAsync(tenant.Id, null, ct);
        var ids = employees.Select(e => e.Id).ToList();
        var credits = services.GetRequiredService<ILeaveCreditService>();
        if (today.Month == 1 && today.Day == 1)
            await credits.CloseYearAsync(tenant.Id, ids, today.Year - 1, null, ct);
        await credits.ExpireCarryAsync(tenant.Id, today.Year, today, ct);
        await credits.CreditAsync(tenant.Id, ids, today.Year, today, false, null, ct);
    }
}
