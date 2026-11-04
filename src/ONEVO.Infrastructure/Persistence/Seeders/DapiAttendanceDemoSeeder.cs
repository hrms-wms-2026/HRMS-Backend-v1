using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only: gives the Dapi smoke tenant a working attendance setup (an active
/// full-company ClockInPolicy, web-enabled work modes) so Clock In / Start Break actually work for
/// a live demo. The attendance history itself comes from DapiHistorySeeder (rolling last 60 days).
/// Idempotent. Must run after DevSmokeTestTenantSeeder (tenant + legal entity must exist).
/// </summary>
public sealed class DapiAttendanceDemoSeeder : IHostedService
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    private static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");

    private static readonly Guid ClockInPolicyId =
        WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-attendance:policy:full-company");

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DapiAttendanceDemoSeeder> _logger;

    public DapiAttendanceDemoSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<DapiAttendanceDemoSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Local `dotnet run` only - see DapiLeaveSampleSeeder for why Test/CI must never run this.
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
            _logger.LogInformation("Dapi attendance setup seeded (clock-in policy, web-enabled work modes).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DapiAttendanceDemoSeeder failed. Attendance demo data was skipped; host continues.");
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

        var legalEntity = await db.LegalEntities
            .FirstOrDefaultAsync(e => e.Id == DapiLegalEntityId && e.TenantId == DapiTenantId, ct);
        if (legalEntity is null)
            return;

        var now = DateTimeOffset.UtcNow;
        await EnsureClockInPolicyAsync(db, now, ct);
        await EnsureWorkModesWebEnabledAsync(db, ct);
    }

    private static async Task EnsureClockInPolicyAsync(
        ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var hasActiveFullCompanyPolicy = await db.ClockInPolicies.AnyAsync(
            p => p.TenantId == DapiTenantId
                && p.LegalEntityId == DapiLegalEntityId
                && p.ScopeType == ClockInPolicy.ScopeFullCompany
                && p.IsActive,
            ct);
        if (hasActiveFullCompanyPolicy)
            return;

        db.ClockInPolicies.Add(new ClockInPolicy
        {
            Id = ClockInPolicyId,
            TenantId = DapiTenantId,
            LegalEntityId = DapiLegalEntityId,
            Name = "Dapi Standard Clock-In Policy",
            ScopeType = ClockInPolicy.ScopeFullCompany,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = null,
            CorrectionRequiresApproval = false,
            NotificationRecipientResolver = ClockInPolicy.NotificationManagementCoverageOwner,
            IsActive = true,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    /// <summary>Only ever flips a mode's web/tray flags on, never off - never undoes a deliberate
    /// admin choice, just unblocks browser/desktop clock-in for a mode that allows neither yet.</summary>
    private static async Task EnsureWorkModesWebEnabledAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var modes = await db.TimeAttendanceWorkModes
            .Where(m => m.TenantId == DapiTenantId && m.LegalEntityId == DapiLegalEntityId && m.IsActive)
            .ToListAsync(ct);

        foreach (var mode in modes)
        {
            if (mode.WebEnabled)
                continue;
            mode.WebEnabled = true;
            mode.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
