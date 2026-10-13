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
/// full-company ClockInPolicy, web-enabled work modes) plus recent clock-in/out history for the
/// tenant owner and Kiru Balachandran, so the dashboard's attendance-driven cards (Today's
/// Schedule, Work Pattern, Monthly Summary, Attendance Trend) aren't empty and Clock In/Start
/// Break actually work for a live demo. Idempotent via deterministic Guids and date-scoped
/// existence checks. Must run after DapiOrgStructureSeeder (owner + employee records must exist).
/// </summary>
public sealed class DapiAttendanceDemoSeeder : IHostedService
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    private static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    private const string KiruEmail = "kiru.balachandran@dapi.test";

    private static readonly Guid ClockInPolicyId =
        WorkManagementDapiDemoSeeder.DeterministicGuid("dapi-attendance:policy:full-company");

    private const int PastWorkdaysToSeed = 5;

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
            _logger.LogInformation("Dapi attendance demo data seeded (clock-in policy, work modes, history).");
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
        var timezone = ResolveColomboTimeZone();
        var workStart = legalEntity.WorkStartTime ?? new TimeOnly(9, 0);
        var workEnd = legalEntity.WorkEndTime ?? new TimeOnly(18, 0);
        var requiredWorkMinutes = (int)(workEnd.ToTimeSpan() - workStart.ToTimeSpan()).TotalMinutes;
        if (requiredWorkMinutes <= 0)
            requiredWorkMinutes = 540;

        await EnsureClockInPolicyAsync(db, now, ct);
        await EnsureWorkModesWebEnabledAsync(db, ct);

        var owner = await db.Employees
            .FirstOrDefaultAsync(e => e.TenantId == DapiTenantId && e.UserId == DapiOwnerUserId, ct);
        var kiru = await db.Employees
            .FirstOrDefaultAsync(e => e.TenantId == DapiTenantId && e.Email == KiruEmail, ct);

        var legalEntityModes = await db.TimeAttendanceWorkModes
            .Where(m => m.TenantId == DapiTenantId && m.LegalEntityId == DapiLegalEntityId && m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
        // Matches each employee's live-resolved default ("onsite") rather than just picking
        // display-order 0, so the seeded history doesn't disagree with what they'd see on a
        // day with no attendance row yet.
        var onsiteMode =
            legalEntityModes.FirstOrDefault(m => m.Name.Contains("onsite", StringComparison.OrdinalIgnoreCase))
            ?? legalEntityModes.FirstOrDefault();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timezone).DateTime);

        foreach (var employee in new[] { owner, kiru })
        {
            if (employee is null)
                continue;

            foreach (var date in PastWorkdays(today, PastWorkdaysToSeed))
            {
                await EnsureCompletedDayAsync(
                    db, employee.Id, date, workStart, workEnd, requiredWorkMinutes,
                    timezone, onsiteMode, now, ct);
            }

            await EnsureTodayInProgressAsync(
                db, employee.Id, today, workStart, workEnd, requiredWorkMinutes,
                timezone, onsiteMode, now, ct);
        }
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

    private static async Task EnsureCompletedDayAsync(
        ApplicationDbContext db, Guid employeeId, DateOnly date, TimeOnly workStart, TimeOnly workEnd,
        int requiredWorkMinutes, TimeZoneInfo timezone, WorkMode? mode, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.AttendanceRecords.FirstOrDefaultAsync(
            r => r.TenantId == DapiTenantId && r.EmployeeId == employeeId && r.Date == date, ct);
        if (existing is not null)
        {
            // Re-run after a work-mode naming fix: patch the cosmetic expected-mode fields on a
            // row this seeder already created, never the clock times, so it stays idempotent.
            if (mode is not null && existing.ExpectedWorkModeId != mode.Id)
            {
                existing.ExpectedWorkModeId = mode.Id;
                existing.ExpectedWorkModeName = mode.Name;
                existing.UpdatedAt = now;
            }
            return;
        }

        var start = ToUtc(date, workStart, timezone);
        var end = ToUtc(date, workEnd, timezone);

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = WorkManagementDapiDemoSeeder.DeterministicGuid(
                $"dapi-attendance:record:{employeeId:N}:{date:yyyy-MM-dd}"),
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            Date = date,
            ExpectedWorkingDay = true,
            WorkTimeType = AttendanceRecord.WorkTimeTypeFixed,
            ScheduledStart = workStart,
            ScheduledEnd = workEnd,
            RequiredWorkMinutes = requiredWorkMinutes,
            ExpectedWorkModeId = mode?.Id,
            ExpectedWorkModeName = mode?.Name ?? "Onsite",
            ScheduleTimezone = timezone.Id,
            IsHoliday = false,
            HolidayName = null,
            ActualStart = start,
            ActualEnd = end,
            WorkedMinutes = requiredWorkMinutes,
            BreakMinutes = 60,
            LateMinutes = 0,
            AttendanceSource = AttendanceRecord.SourceWeb,
            Status = AttendanceRecord.StatusOnTime,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task EnsureTodayInProgressAsync(
        ApplicationDbContext db, Guid employeeId, DateOnly today, TimeOnly workStart, TimeOnly workEnd,
        int requiredWorkMinutes, TimeZoneInfo timezone, WorkMode? mode, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.AttendanceRecords.FirstOrDefaultAsync(
            r => r.TenantId == DapiTenantId && r.EmployeeId == employeeId && r.Date == today, ct);
        if (existing is not null)
        {
            if (mode is not null && existing.ExpectedWorkModeId != mode.Id)
            {
                existing.ExpectedWorkModeId = mode.Id;
                existing.ExpectedWorkModeName = mode.Name;
                existing.UpdatedAt = now;
            }
            return;
        }

        var start = ToUtc(today, workStart, timezone);
        if (start > now)
            start = now.AddMinutes(-30);

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = WorkManagementDapiDemoSeeder.DeterministicGuid(
                $"dapi-attendance:record:{employeeId:N}:{today:yyyy-MM-dd}"),
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            Date = today,
            ExpectedWorkingDay = true,
            WorkTimeType = AttendanceRecord.WorkTimeTypeFixed,
            ScheduledStart = workStart,
            ScheduledEnd = workEnd,
            RequiredWorkMinutes = requiredWorkMinutes,
            ExpectedWorkModeId = mode?.Id,
            ExpectedWorkModeName = mode?.Name ?? "Onsite",
            ScheduleTimezone = timezone.Id,
            IsHoliday = false,
            HolidayName = null,
            ActualStart = start,
            ActualEnd = null,
            WorkedMinutes = 0,
            BreakMinutes = 0,
            LateMinutes = 0,
            AttendanceSource = AttendanceRecord.SourceWeb,
            Status = AttendanceRecord.StatusOnTime,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static IEnumerable<DateOnly> PastWorkdays(DateOnly before, int count)
    {
        var found = 0;
        var cursor = before.AddDays(-1);
        while (found < count)
        {
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                yield return cursor;
                found++;
            }
            cursor = cursor.AddDays(-1);
        }
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly localTime, TimeZoneInfo timezone)
    {
        var unspecified = DateTime.SpecifyKind(date.ToDateTime(localTime), DateTimeKind.Unspecified);
        var offset = timezone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveColomboTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
