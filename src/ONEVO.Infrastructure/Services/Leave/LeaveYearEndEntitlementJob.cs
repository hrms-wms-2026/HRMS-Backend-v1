using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Infrastructure.Services.Leave;

/// <summary>
/// Daily-checked job that generates the new year's Leave entitlements (with carry-forward and
/// forfeiture already computed by LeaveEntitlementCalculator) for every active tenant, once,
/// on Jan 1 UTC. Same BackgroundService/daily-check shape as ActivityDailySummaryJob; same
/// admin-mode tenant enumeration + per-tenant SwitchToTenantAsync shape as
/// BulkOnboardingBatchProcessor. Does not call IMediator - GenerateEntitlementsCommandHandler
/// depends on ICurrentUser, which is HTTP-context-bound and unavailable here.
/// </summary>
public sealed class LeaveYearEndEntitlementJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeOnly TargetUtcTime = new(1, 0);

    private readonly IServiceProvider _services;
    private readonly ILogger<LeaveYearEndEntitlementJob> _logger;
    private int? _lastProcessedYear;

    public LeaveYearEndEntitlementJob(IServiceProvider services, ILogger<LeaveYearEndEntitlementJob> logger)
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
                if (now.Month == 1 && now.Day == 1
                    && now.TimeOfDay >= TargetUtcTime.ToTimeSpan()
                    && _lastProcessedYear != now.Year)
                {
                    await RunForYearAsync(now.Year, stoppingToken);
                    _lastProcessedYear = now.Year;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leave year-end entitlement job iteration failed; will retry.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Public entry for tests / manual triggers - same precedent as
    /// ActivityDailySummaryJob.RunAggregationAsync.</summary>
    public async Task RunForYearAsync(int year, CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        tenantContext.SetAdminMode();

        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenants = await tenantRepository.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct);

        _logger.LogInformation("Leave year-end entitlement job started. Year={Year} TenantCount={Count}", year, tenants.Count);

        var processedTenants = 0;
        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ProcessTenantAsync(scope.ServiceProvider, tenant, year, ct);
                processedTenants++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leave year-end generation failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }

        _logger.LogInformation(
            "Leave year-end entitlement job finished. Year={Year} ProcessedTenants={Processed}/{Total}",
            year, processedTenants, tenants.Count);
    }

    private static async Task ProcessTenantAsync(
        IServiceProvider services, Tenant tenant, int year, CancellationToken ct)
    {
        var tenantSwitcher = services.GetRequiredService<ITenantContextSwitcher>();
        await tenantSwitcher.SwitchToTenantAsync(
            new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var planner = services.GetRequiredService<LeaveEntitlementPlanner>();
        var entitlements = services.GetRequiredService<ILeaveEntitlementRepository>();
        var clock = services.GetRequiredService<IDateTimeProvider>();

        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var plan = await planner.PlanAsync(tenant.Id, year, legalEntityId: null, asOfDate, ct);
        if (plan.Lines.Count == 0)
            return;

        var now = clock.UtcNow;
        var writeSets = plan.Lines.Select(line =>
        {
            var entitlement = new LeaveEntitlement
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EmployeeId = line.EmployeeId,
                LeaveTypeId = line.LeaveTypeId,
                Year = year,
                TotalDays = line.TotalDays,
                UsedDays = 0m,
                PendingDays = 0m,
                CarriedForwardDays = line.CarriedForwardDays,
                Source = LeaveEntitlementSources.Auto,
                CreatedAt = now
            };

            var audits = new List<LeaveBalanceAudit>
            {
                new()
                {
                    Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = line.EmployeeId, LeaveTypeId = line.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Accrual,
                    DaysChanged = line.TotalDays + line.CarriedForwardDays,
                    BalanceAfter = line.TotalDays + line.CarriedForwardDays,
                    Reason = "Year-end automatic generation", CreatedAt = now, CreatedBy = null
                }
            };

            if (line.ForfeitedDays > 0m)
            {
                audits.Add(new LeaveBalanceAudit
                {
                    Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = line.EmployeeId, LeaveTypeId = line.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Forfeiture,
                    DaysChanged = -line.ForfeitedDays,
                    BalanceAfter = line.TotalDays + line.CarriedForwardDays,
                    Reason = "Carry-forward cap applied during year-end generation",
                    CreatedAt = now, CreatedBy = null
                });
            }

            return new LeaveEntitlementWriteSet(entitlement, audits);
        }).ToList();

        try
        {
            await entitlements.AddGeneratedAsync(writeSets, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            // Already generated for this tenant/year (job re-ran, or HR already generated
            // manually) - idempotent no-op, matches spec §4 "Year-end already ran: Skip".
        }
    }
}
