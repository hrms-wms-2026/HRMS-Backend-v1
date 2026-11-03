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
/// Development/Test-only: seeds leave types, one company-wide policy per type, and 2026 entitlements
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

        await EnsureTypeAsync(db, AnnualTypeId, "Annual Leave", "ANNUAL", "#2563EB", now, ct);
        await EnsureTypeAsync(db, SickTypeId, "Sick Leave", "SICK", "#16A34A", now, ct);
        await EnsureTypeAsync(db, CasualTypeId, "Casual Leave", "CASUAL", "#475569", now, ct);

        await EnsurePolicyAsync(db, AnnualTypeId, "Annual Leave · Dapi standard", 14m * workHours, workHours, now, ct);
        await EnsurePolicyAsync(db, SickTypeId, "Sick Leave · Dapi standard", 10m * workHours, workHours, now, ct);
        await EnsurePolicyAsync(db, CasualTypeId, "Casual Leave · Dapi standard", 5m * workHours, workHours, now, ct);

        var policyVersions = await (
            from policy in db.LeavePolicies
            join version in db.LeavePolicyVersions on policy.Id equals version.LeavePolicyId
            where policy.TenantId == DapiTenantId && version.EffectiveFrom <= new DateOnly(SeedYear, 12, 31)
            orderby version.EffectiveFrom descending
            select new { policy.LeaveTypeId, PolicyId = policy.Id, VersionId = version.Id })
            .ToListAsync(ct);
        var policyByType = policyVersions.GroupBy(x => x.LeaveTypeId)
            .ToDictionary(g => g.Key, g => g.First());

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
                policyByType.TryGetValue(typeId, out var policy);
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
                    LeavePolicyId = policy?.PolicyId,
                    LeavePolicyVersionId = policy?.VersionId,
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
                    Reason = "Generated from Dapi standard leave policies",
                    LeavePolicyId = policy?.PolicyId,
                    LeavePolicyVersionId = policy?.VersionId,
                    Year = SeedYear,
                    CoversFromMonth = 1,
                    CoversToMonth = 12,
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
        string color,
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
            Color = color,
            IsPaid = true,
            ApplicableGender = LeaveGenderRestrictions.All,
            AcceptedDocumentTypes = [],
            IsActive = true,
            CreatedAt = now
        });
    }

    /// <summary>One single-type policy per leave type, assigned to the whole company. Skipped when the
    /// tenant already has any policy for that leave type (e.g. one split out by the policy migration).</summary>
    private static async Task EnsurePolicyAsync(
        ApplicationDbContext db,
        Guid typeId,
        string name,
        decimal hoursPerYear,
        decimal workHours,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (await db.LeavePolicies.AnyAsync(p => p.TenantId == DapiTenantId && p.LeaveTypeId == typeId, ct))
            return;

        var policyId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-leave:policy:{typeId:N}");
        var effectiveFrom = new DateOnly(SeedYear, 1, 1);
        db.LeavePolicies.Add(new LeavePolicy
        {
            Id = policyId,
            TenantId = DapiTenantId,
            LeaveTypeId = typeId,
            Name = name,
            Description = "Default policy for Dapi Technologies.",
            IsActive = true,
            CreatedAt = now
        });
        db.LeavePolicyVersions.Add(new LeavePolicyVersion
        {
            Id = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-leave:policy-version:{typeId:N}:1"),
            TenantId = DapiTenantId,
            LeavePolicyId = policyId,
            VersionNumber = 1,
            EffectiveFrom = effectiveFrom,
            AmountHours = decimal.Round(hoursPerYear, 2, MidpointRounding.AwayFromZero),
            AmountPeriod = LeaveAmountPeriods.Year,
            EligibleFrom = LeaveEligibility.Immediately,
            MinRequestHours = decimal.Round(workHours / 2m, 2, MidpointRounding.AwayFromZero),
            ApprovalMode = LeaveApprovalModes.AnyOne,
            CreatedAt = now,
            CreatedBy = DapiOwnerUserId
        });
        db.LeavePolicyAssignments.Add(new LeavePolicyAssignment
        {
            Id = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-leave:policy-assignment:{typeId:N}"),
            TenantId = DapiTenantId,
            LeavePolicyId = policyId,
            LeaveTypeId = typeId,
            TargetKind = LeavePolicyTargetKinds.LegalEntity,
            TargetId = DapiLegalEntityId,
            EffectiveFrom = effectiveFrom,
            CreatedAt = now,
            CreatedBy = DapiOwnerUserId
        });
    }
}
