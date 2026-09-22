using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only: seeds leave types, one legal-entity policy, and 2026 entitlements
/// on the dapi smoke tenant so Time Off screens are not empty. Idempotent via deterministic
/// Guids. Must run after DevSmokeTestTenantSeeder and WorkManagementDapiDemoSeeder.
/// </summary>
public sealed class DapiLeaveSampleSeeder : IHostedService
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    private static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    private static readonly Guid AnnualTypeId = WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-leave:type:annual");
    private static readonly Guid SickTypeId = WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-leave:type:sick");
    private static readonly Guid CasualTypeId = WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-leave:type:casual");
    private static readonly Guid PolicyId = WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-leave:policy:standard");

    private const int SeedYear = 2026;

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DapiLeaveSampleSeeder> _logger;

    public DapiLeaveSampleSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<DapiLeaveSampleSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Local `dotnet run` only. Must not run in Test/CI — integration hosts boot every
        // IHostedService, and a throw or a blocked SaveChanges here hangs ApiBoot/Leave tests
        // until the 10-minute blame-hang timeout.
        if (!_environment.IsDevelopment())
            return;

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

            tenantContext.SetAdminMode();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await SeedAsync(db, tenantContext, timeout.Token);
            await db.SaveChangesAsync(timeout.Token);
            _logger.LogInformation("Dapi leave sample data seeded (types, policy, {Year} entitlements).", SeedYear);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DapiLeaveSampleSeeder failed. Leave sample data was skipped; host continues.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (tenant is null)
            return;

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(
            tenant.Id, tenant.Slug, tenant.Status, PlanCode: null));

        var now = DateTimeOffset.UtcNow;
        var legalEntity = await db.LegalEntities
            .FirstOrDefaultAsync(e => e.Id == DapiLegalEntityId && e.TenantId == DapiTenantId, ct);
        if (legalEntity is null)
            return;

        if (legalEntity.WorkStartTime is null || legalEntity.WorkEndTime is null)
        {
            legalEntity.WorkStartTime = new TimeOnly(9, 0);
            legalEntity.WorkEndTime = new TimeOnly(18, 0);
            legalEntity.BreakDurationMinutes ??= 60;
        }

        var workHours = WorkDayHoursCalculator.TryCompute(
            legalEntity.WorkStartTime, legalEntity.WorkEndTime, legalEntity.BreakDurationMinutes) ?? 8m;
        if (workHours <= 0m)
            workHours = 8m;

        await EnsureTypeAsync(db, AnnualTypeId, "Annual Leave", "ANNUAL", LeaveTypeCategories.Annual, 14m, now, ct);
        await EnsureTypeAsync(db, SickTypeId, "Sick Leave", "SICK", LeaveTypeCategories.Sick, 10m, now, ct);
        await EnsureTypeAsync(db, CasualTypeId, "Casual Leave", "CASUAL", LeaveTypeCategories.Custom, 5m, now, ct);

        var policy = await db.LeavePolicies.FirstOrDefaultAsync(p => p.Id == PolicyId, ct);
        if (policy is null)
        {
            policy = new LeavePolicy
            {
                Id = PolicyId,
                TenantId = DapiTenantId,
                Name = "Dapi Standard Leave",
                Description = "Default paid leave policy for Dapi Technologies.",
                AccrualMethod = LeaveAccrualMethods.Annual,
                AccrualStart = LeaveAccrualStarts.Immediately,
                ProrationMethod = LeaveProrationMethods.CalendarDays,
                ApprovalMode = LeaveApprovalModes.AnyOne,
                MinDaysPerRequest = 0.5m,
                EffectiveFrom = new DateOnly(SeedYear, 1, 1),
                Version = 1,
                IsActive = true,
                CreatedAt = now
            };
            db.LeavePolicies.Add(policy);
        }

        await EnsurePolicyTypeAsync(db, PolicyId, AnnualTypeId, 14m, ct);
        await EnsurePolicyTypeAsync(db, PolicyId, SickTypeId, 10m, ct);
        await EnsurePolicyTypeAsync(db, PolicyId, CasualTypeId, 5m, ct);

        var assignmentId = WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-leave:policy-le:standard");
        var assignment = await db.LeavePolicyLegalEntities.FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null)
        {
            db.LeavePolicyLegalEntities.Add(new LeavePolicyLegalEntity
            {
                Id = assignmentId,
                TenantId = DapiTenantId,
                LeavePolicyId = PolicyId,
                LegalEntityId = DapiLegalEntityId,
                EffectiveDate = new DateOnly(SeedYear, 1, 1),
                IsActive = true
            });
        }

        var typeHours = new (Guid TypeId, decimal Days)[]
        {
            (AnnualTypeId, 14m),
            (SickTypeId, 10m),
            (CasualTypeId, 5m)
        };

        var employeeIds = await db.Employees
            .Where(e => e.TenantId == DapiTenantId && e.LegalEntityId == DapiLegalEntityId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var employeeId in employeeIds)
        {
            foreach (var (typeId, days) in typeHours)
            {
                var exists = await db.LeaveEntitlements.AnyAsync(
                    e => e.TenantId == DapiTenantId
                        && e.EmployeeId == employeeId
                        && e.LeaveTypeId == typeId
                        && e.Year == SeedYear,
                    ct);
                if (exists)
                    continue;

                var hours = decimal.Round(days * workHours, 2, MidpointRounding.AwayFromZero);
                var entitlementId = WorkManagementDapiDemoSeeder.DeterministicGuid(
                    $"dapi-leave:entitlement:{employeeId:N}:{typeId:N}:{SeedYear}");
                db.LeaveEntitlements.Add(new LeaveEntitlement
                {
                    Id = entitlementId,
                    TenantId = DapiTenantId,
                    EmployeeId = employeeId,
                    LeaveTypeId = typeId,
                    Year = SeedYear,
                    TotalHours = hours,
                    UsedHours = 0m,
                    PendingHours = 0m,
                    CarriedForwardHours = 0m,
                    Source = LeaveEntitlementSources.Auto,
                    CreatedAt = now
                });
                db.LeaveBalanceAudits.Add(new LeaveBalanceAudit
                {
                    Id = WorkManagementDapiDemoSeeder.DeterministicGuid(
                        $"dapi-leave:audit:{employeeId:N}:{typeId:N}:{SeedYear}"),
                    TenantId = DapiTenantId,
                    EmployeeId = employeeId,
                    LeaveTypeId = typeId,
                    ChangeType = LeaveBalanceChangeTypes.Accrual,
                    HoursChanged = hours,
                    BalanceAfter = hours,
                    Reason = "Generated from Dapi Standard Leave policy",
                    CreatedAt = now,
                    CreatedBy = DapiOwnerUserId
                });
            }
        }
    }

    private static async Task EnsureTypeAsync(
        ApplicationDbContext db,
        Guid id,
        string name,
        string code,
        string category,
        decimal days,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (await db.LeaveTypes.AnyAsync(t => t.Id == id || (t.TenantId == DapiTenantId && t.Code == code), ct))
            return;

        db.LeaveTypes.Add(new LeaveType
        {
            Id = id,
            TenantId = DapiTenantId,
            Name = name,
            Code = code,
            Category = category,
            IsPaid = true,
            RequiresApproval = true,
            DefaultDaysPerYear = days,
            ApplicableGender = LeaveGenderRestrictions.All,
            AcceptedDocumentTypes = [],
            IsActive = true,
            CreatedAt = now
        });
    }

    private static async Task EnsurePolicyTypeAsync(
        ApplicationDbContext db,
        Guid policyId,
        Guid typeId,
        decimal days,
        CancellationToken ct)
    {
        var exists = await db.LeavePolicyLeaveTypes.AnyAsync(
            r => r.TenantId == DapiTenantId && r.LeavePolicyId == policyId && r.LeaveTypeId == typeId, ct);
        if (exists)
            return;

        db.LeavePolicyLeaveTypes.Add(new LeavePolicyLeaveType
        {
            Id = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-leave:policy-type:{typeId:N}"),
            TenantId = DapiTenantId,
            LeavePolicyId = policyId,
            LeaveTypeId = typeId,
            AnnualEntitlementDays = days
        });
    }
}
