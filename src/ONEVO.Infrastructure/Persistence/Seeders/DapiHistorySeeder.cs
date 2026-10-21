using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;
using ExceptionStatus = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.ExceptionStatus;
using ExceptionType = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.ExceptionType;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only, idempotent September and October 2026 history for every active employee in the Dapi
/// demo tenant. This supplies the historical attendance, monitoring, application-usage,
/// verification-alert, leave, and leave-approval data consumed by employee and manager screens.
/// It must run after the Dapi tenant, org structure, and leave configuration seeders.
/// </summary>
public sealed class DapiHistorySeeder : IHostedService
{
    internal static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    internal static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    internal static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    internal static readonly DateOnly[] SeedMonthStarts =
    [
        new(2026, 9, 1),
        new(2026, 10, 1)
    ];

    private static readonly TimeSpan ColomboOffset = TimeSpan.FromMinutes(330);

    private static readonly (string ProcessName, string Category)[] SeedApplications =
    [
        ("chrome.exe", GlobalApplicationCategories.Browser),
        ("ms-teams.exe", GlobalApplicationCategories.Meeting),
        ("code.exe", GlobalApplicationCategories.Development),
        ("winword.exe", GlobalApplicationCategories.Documentation),
        ("excel.exe", GlobalApplicationCategories.Documentation),
        ("outlook.exe", GlobalApplicationCategories.Communication)
    ];

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DapiHistorySeeder> _logger;

    public DapiHistorySeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<DapiHistorySeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return;

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

            tenantContext.SetAdminMode();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            // The real leave route (coverage, reporting line, HR fallback, delegation). A hosted service has no
            // signed-in user, so the authority resolver is pinned to the Dapi tenant instead of read from ICurrentUser.
            var approvers = ActivatorUtilities.CreateInstance<LeaveApproverResolver>(
                scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IEmployeeAuthorityResolverFactory>().CreateForTenant(DapiTenantId));
            var employeeCount = await SeedAsync(db, tenantContext, timeout.Token, approvers);
            _logger.LogInformation(
                "Dapi September-October 2026 history seeded for {EmployeeCount} employees.",
                employeeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dapi September-October history seeding failed. Startup will continue.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<int> SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        CancellationToken ct,
        ILeaveApproverResolver? approvers = null)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (tenant is null)
            return 0;

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(
            tenant.Id,
            tenant.Slug,
            tenant.Status,
            PlanCode: null));

        var employees = await db.Employees
            .Where(e => e.TenantId == DapiTenantId
                && e.LegalEntityId == DapiLegalEntityId
                && !e.IsDeleted
                && e.TerminationDate == null)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(ct);
        if (employees.Count == 0)
            return 0;

        var leaveTypes = await db.LeaveTypes
            .Where(t => t.TenantId == DapiTenantId && t.IsActive)
            .OrderBy(t => t.Code)
            .ToListAsync(ct);
        if (leaveTypes.Count == 0)
            return 0;

        var ownerEmployeeId = employees
            .Where(e => e.UserId == DapiOwnerUserId)
            .Select(e => e.Id)
            .FirstOrDefault();
        if (ownerEmployeeId == Guid.Empty)
            ownerEmployeeId = employees[0].Id;

        var fallbackApproverId = employees.First(e => e.Id != ownerEmployeeId).Id;
        var workMode = await db.TimeAttendanceWorkModes
            .Where(w => w.TenantId == DapiTenantId
                && w.LegalEntityId == DapiLegalEntityId
                && w.IsActive)
            .OrderBy(w => w.Name)
            .FirstOrDefaultAsync(ct);

        await EnsureGlobalApplicationsAsync(db, ct);

        foreach (var monthStart in SeedMonthStarts)
        {
            await SeedMonthAsync(
                db,
                employees,
                leaveTypes,
                ownerEmployeeId,
                fallbackApproverId,
                workMode?.Id,
                workMode?.Name,
                monthStart,
                approvers,
                ct);
        }

        return employees.Count;
    }

    private static async Task SeedMonthAsync(
        ApplicationDbContext db,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<LeaveType> leaveTypes,
        Guid ownerEmployeeId,
        Guid fallbackApproverId,
        Guid? workModeId,
        string? workModeName,
        DateOnly monthStart,
        ILeaveApproverResolver? approvers,
        CancellationToken ct)
    {
        var monthEnd = new DateOnly(
            monthStart.Year,
            monthStart.Month,
            DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
        var rangeStart = At(monthStart, 0, 0);
        var rangeEnd = At(monthEnd.AddDays(1), 0, 0);
        var allDates = Enumerable.Range(0, monthEnd.Day)
            .Select(monthStart.AddDays)
            .ToArray();
        var workdays = allDates
            .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .ToArray();

        var attendanceKeys = (await db.AttendanceRecords
                .Where(x => x.TenantId == DapiTenantId && x.Date >= monthStart && x.Date <= monthEnd)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync(ct))
            .Select(x => (x.EmployeeId, x.Date))
            .ToHashSet();
        var presenceKeys = (await db.PresenceSessions
                .Where(x => x.TenantId == DapiTenantId && x.Date >= monthStart && x.Date <= monthEnd)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync(ct))
            .Select(x => (x.EmployeeId, x.Date))
            .ToHashSet();
        var summaryKeys = (await db.ActivityDailySummaries
                .Where(x => x.TenantId == DapiTenantId && x.Date >= monthStart && x.Date <= monthEnd)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync(ct))
            .Select(x => (x.EmployeeId, x.Date))
            .ToHashSet();

        var existingBreakIds = await ExistingIdsAsync(
            db.BreakRecords.Where(x => x.TenantId == DapiTenantId
                && x.BreakStart >= rangeStart && x.BreakStart < rangeEnd).Select(x => x.Id), ct);
        var existingActivityIds = await ExistingIdsAsync(
            db.ActivitySnapshots.Where(x => x.TenantId == DapiTenantId
                && x.CapturedAt >= rangeStart && x.CapturedAt < rangeEnd).Select(x => x.Id), ct);
        var existingAppUsageIds = await ExistingIdsAsync(
            db.AppUsageSnapshots.Where(x => x.TenantId == DapiTenantId
                && x.CapturedAt >= rangeStart && x.CapturedAt < rangeEnd).Select(x => x.Id), ct);
        var existingDeviceStateIds = await ExistingIdsAsync(
            db.DeviceStateSnapshots.Where(x => x.TenantId == DapiTenantId
                && x.CapturedAt >= rangeStart && x.CapturedAt < rangeEnd).Select(x => x.Id), ct);
        var existingFaceAttemptIds = await ExistingIdsAsync(
            db.FaceVerificationAttempts.Where(x => x.TenantId == DapiTenantId
                && x.CreatedAt >= rangeStart && x.CreatedAt < rangeEnd).Select(x => x.Id), ct);
        var existingExceptionIds = await ExistingIdsAsync(
            db.Exceptions.Where(x => x.TenantId == DapiTenantId
                && x.DetectedAt >= rangeStart && x.DetectedAt < rangeEnd).Select(x => x.Id), ct);
        var existingNotifications = await db.MonitoringNotifications
            .Where(x => x.TenantId == DapiTenantId
                && x.CreatedAt >= rangeStart && x.CreatedAt < rangeEnd)
            .ToDictionaryAsync(x => x.Id, ct);
        var existingLeaveRequestIds = await ExistingIdsAsync(
            db.LeaveRequests.Where(x => x.TenantId == DapiTenantId
                && x.StartAt >= rangeStart && x.StartAt < rangeEnd).Select(x => x.Id), ct);
        var existingApproverIds = await ExistingIdsAsync(
            db.LeaveRequestApprovers.Where(x => x.TenantId == DapiTenantId).Select(x => x.Id), ct);
        var existingAllocationIds = await ExistingIdsAsync(
            db.LeaveRequestDayAllocations.Where(x => x.TenantId == DapiTenantId).Select(x => x.Id), ct);
        var existingAuditIds = await ExistingIdsAsync(
            db.LeaveBalanceAudits.Where(x => x.TenantId == DapiTenantId).Select(x => x.Id), ct);

        for (var index = 0; index < employees.Count; index++)
        {
            var employee = employees[index];
            var status = (index % 6) switch
            {
                1 => LeaveRequestStatuses.Rejected,
                2 => LeaveRequestStatuses.Pending,
                _ => LeaveRequestStatuses.Approved
            };
            var leaveDate = workdays[(4 + index * 3) % workdays.Length];
            var approverId = await ResolveApproverAsync(approvers, employee.Id, leaveDate, ct)
                ?? (employee.Id == ownerEmployeeId ? fallbackApproverId : ownerEmployeeId);
            var plan = new LeavePlan(
                leaveDate,
                leaveTypes[index % leaveTypes.Count].Id,
                status,
                approverId);
            await SeedLeaveHistoryAsync(
                db,
                employee.Id,
                plan,
                existingLeaveRequestIds,
                existingApproverIds,
                existingAllocationIds,
                existingAuditIds,
                ct);

            var verificationDate = workdays[(2 + index * 5) % workdays.Length];
            foreach (var date in allDates)
            {
                var isWorkingDay = date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
                var isApprovedLeave = plan.Status == LeaveRequestStatuses.Approved && plan.Date == date;

                SeedAttendance(
                    db,
                    employee.Id,
                    workModeId,
                    workModeName,
                    index,
                    date,
                    isWorkingDay,
                    isApprovedLeave,
                    attendanceKeys,
                    presenceKeys,
                    existingBreakIds);

                if (isWorkingDay && !isApprovedLeave)
                {
                    SeedMonitoringDay(
                        db,
                        employee.Id,
                        index,
                        date,
                        summaryKeys,
                        existingActivityIds,
                        existingAppUsageIds,
                        existingDeviceStateIds);
                }

                if (isWorkingDay)
                {
                    SeedVerificationAttempts(
                        db,
                        employee.Id,
                        date,
                        verificationDate == date,
                        existingFaceAttemptIds);
                }
            }

            SeedVerificationAlert(
                db,
                employee.Id,
                index,
                verificationDate,
                existingExceptionIds,
                existingNotifications);

            if ((index + 1) % 5 == 0)
                await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureGlobalApplicationsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var processNames = SeedApplications.Select(x => x.ProcessName).ToArray();
        var existing = (await db.GlobalApplications
            .Where(x => processNames.Contains(x.ProcessName))
            .ToListAsync(ct))
            .ToDictionary(x => x.ProcessName, StringComparer.OrdinalIgnoreCase);
        var firstSeen = At(SeedMonthStarts[0], 9, 0);
        var finalMonth = SeedMonthStarts[^1];
        var finalDate = new DateOnly(
            finalMonth.Year,
            finalMonth.Month,
            DateTime.DaysInMonth(finalMonth.Year, finalMonth.Month));
        var lastSeen = At(finalDate, 17, 0);

        foreach (var (processName, category) in SeedApplications)
        {
            if (existing.TryGetValue(processName, out var application))
            {
                if (application.FirstSeenAt > firstSeen)
                    application.FirstSeenAt = firstSeen;
                if (application.LastSeenAt < lastSeen)
                    application.LastSeenAt = lastSeen;
                continue;
            }

            db.GlobalApplications.Add(new GlobalApplication
            {
                Id = SeedId($"global-app:{processName}"),
                AppName = GlobalApplication.DefaultDisplayName(processName),
                ProcessName = processName,
                Category = category,
                IsPublic = true,
                IsProductiveDefault = category != GlobalApplicationCategories.Meeting,
                FirstSeenAt = firstSeen,
                LastSeenAt = lastSeen,
                CreatedAt = firstSeen
            });
        }
    }

    private static void SeedAttendance(
        ApplicationDbContext db,
        Guid employeeId,
        Guid? workModeId,
        string? workModeName,
        int employeeIndex,
        DateOnly date,
        bool isWorkingDay,
        bool isApprovedLeave,
        HashSet<(Guid EmployeeId, DateOnly Date)> attendanceKeys,
        HashSet<(Guid EmployeeId, DateOnly Date)> presenceKeys,
        HashSet<Guid> existingBreakIds)
    {
        var key = (employeeId, date);
        var lateMinutes = (employeeIndex + date.Day) % 6 == 0 ? 20 : 0;
        var shortHours = (employeeIndex + date.Day) % 13 == 0;
        var clockIn = At(date, 9, lateMinutes);
        var clockOut = shortHours ? At(date, 16, 30) : At(date, 18, 0);
        const int breakMinutes = 60;
        var workedMinutes = Math.Max(0, (int)(clockOut - clockIn).TotalMinutes - breakMinutes);

        if (!attendanceKeys.Contains(key))
        {
            var status = !isWorkingDay
                ? AttendanceRecord.StatusNonWorkingDay
                : isApprovedLeave
                    ? AttendanceRecord.StatusOnTimeOff
                    : shortHours
                        ? AttendanceRecord.StatusShortHours
                        : lateMinutes > 0
                            ? AttendanceRecord.StatusLate
                            : AttendanceRecord.StatusNormal;

            db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = SeedId($"attendance:{employeeId:N}:{date:yyyy-MM-dd}"),
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Date = date,
                ExpectedWorkingDay = isWorkingDay,
                WorkTimeType = AttendanceRecord.WorkTimeTypeFixed,
                ScheduledStart = isWorkingDay ? new TimeOnly(9, 0) : null,
                ScheduledEnd = isWorkingDay ? new TimeOnly(18, 0) : null,
                RequiredWorkMinutes = isWorkingDay ? 480 : null,
                ExpectedWorkModeId = isWorkingDay ? workModeId : null,
                ExpectedWorkModeName = isWorkingDay ? workModeName : null,
                ScheduleTimezone = "Asia/Colombo",
                IsHoliday = false,
                ActualStart = isWorkingDay && !isApprovedLeave ? clockIn : null,
                ActualEnd = isWorkingDay && !isApprovedLeave ? clockOut : null,
                WorkedMinutes = isWorkingDay && !isApprovedLeave ? workedMinutes : 0,
                BreakMinutes = isWorkingDay && !isApprovedLeave ? breakMinutes : 0,
                LateMinutes = isWorkingDay && !isApprovedLeave ? lateMinutes : null,
                AttendanceSource = "agent",
                Status = status,
                CreatedAt = At(date, 18, 5),
                UpdatedAt = At(date, 18, 5)
            });
            attendanceKeys.Add(key);
        }

        if (!isWorkingDay || isApprovedLeave)
            return;

        if (!presenceKeys.Contains(key))
        {
            db.PresenceSessions.Add(new PresenceSession
            {
                Id = SeedId($"presence:{employeeId:N}:{date:yyyy-MM-dd}"),
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Date = date,
                FirstSeenAt = clockIn,
                LastSeenAt = clockOut,
                TotalPresentMinutes = workedMinutes,
                TotalBreakMinutes = breakMinutes,
                Source = "agent",
                Status = "closed",
                CreatedAt = clockIn,
                UpdatedAt = clockOut
            });
            presenceKeys.Add(key);
        }

        var breakId = SeedId($"break:{employeeId:N}:{date:yyyy-MM-dd}");
        if (existingBreakIds.Add(breakId))
        {
            db.BreakRecords.Add(new BreakRecord
            {
                Id = breakId,
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                BreakStart = At(date, 13, 0),
                BreakEnd = At(date, 14, 0),
                BreakType = "lunch",
                AutoDetected = false,
                CreatedAt = At(date, 13, 0)
            });
        }
    }

    private static void SeedMonitoringDay(
        ApplicationDbContext db,
        Guid employeeId,
        int employeeIndex,
        DateOnly date,
        HashSet<(Guid EmployeeId, DateOnly Date)> summaryKeys,
        HashSet<Guid> existingActivityIds,
        HashSet<Guid> existingAppUsageIds,
        HashSet<Guid> existingDeviceStateIds)
    {
        var deviceId = SeedId($"device:{employeeId:N}");
        var processTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var activeSecondsTotal = 0;
        var idleSecondsTotal = 0;
        var keyboardTotal = 0;
        var mouseTotal = 0;
        decimal intensityTotal = 0;

        for (var slot = 0; slot < 12; slot++)
        {
            var minuteOfDay = 9 * 60 + 20 + slot * 40;
            var capturedAt = At(date, minuteOfDay / 60, minuteOfDay % 60);
            var processName = SeedApplications[(employeeIndex + slot) % SeedApplications.Length].ProcessName;
            var activeSeconds = 2_100 - ((employeeIndex + slot) % 3) * 60;
            var idleSeconds = 2_400 - activeSeconds;
            var keyboard = 48 + (employeeIndex + slot) % 35;
            var mouse = 90 + (employeeIndex * 3 + slot) % 55;
            var intensity = decimal.Round((keyboard + mouse) / 2.2m, 2);

            activeSecondsTotal += activeSeconds;
            idleSecondsTotal += idleSeconds;
            keyboardTotal += keyboard;
            mouseTotal += mouse;
            intensityTotal += intensity;
            processTotals[processName] = processTotals.GetValueOrDefault(processName) + activeSeconds;

            var activityId = SeedId($"activity:{employeeId:N}:{date:yyyy-MM-dd}:{slot}");
            if (existingActivityIds.Add(activityId))
            {
                db.ActivitySnapshots.Add(new ActivitySnapshot
                {
                    Id = activityId,
                    TenantId = DapiTenantId,
                    EmployeeId = employeeId,
                    AgentDeviceId = deviceId,
                    CapturedAt = capturedAt,
                    KeyboardEventsCount = keyboard,
                    MouseEventsCount = mouse,
                    ActiveSeconds = activeSeconds,
                    IdleSeconds = idleSeconds,
                    IntensityScore = intensity,
                    ForegroundProcessName = processName,
                    CreatedAt = capturedAt
                });
            }

            var usageId = SeedId($"app-usage:{employeeId:N}:{date:yyyy-MM-dd}:{slot}");
            if (existingAppUsageIds.Add(usageId))
            {
                db.AppUsageSnapshots.Add(new AppUsageSnapshot
                {
                    Id = usageId,
                    TenantId = DapiTenantId,
                    EmployeeId = employeeId,
                    AgentDeviceId = deviceId,
                    CapturedAt = capturedAt,
                    ProcessName = processName,
                    WindowTitleHash = HashTitle($"dapi-demo:{processName}:{employeeId:N}:{date:yyyy-MM-dd}:{slot}"),
                    CreatedAt = capturedAt
                });
            }

            var deviceStateId = SeedId($"device-state:{employeeId:N}:{date:yyyy-MM-dd}:{slot}");
            if (existingDeviceStateIds.Add(deviceStateId))
            {
                db.DeviceStateSnapshots.Add(new DeviceStateSnapshot
                {
                    Id = deviceStateId,
                    TenantId = DapiTenantId,
                    EmployeeId = employeeId,
                    AgentDeviceId = deviceId,
                    CapturedAt = capturedAt,
                    IdleSeconds = idleSeconds,
                    IsIdle = idleSeconds >= 600,
                    Latitude = 6.9271,
                    Longitude = 79.8612,
                    AccuracyMeters = 18,
                    CreatedAt = capturedAt
                });
            }
        }

        var summaryKey = (employeeId, date);
        if (summaryKeys.Contains(summaryKey))
            return;

        var activeMinutes = activeSecondsTotal / 60;
        var idleMinutes = idleSecondsTotal / 60;
        var totalMinutes = Math.Max(1, activeMinutes + idleMinutes);
        var topApps = processTotals
            .OrderByDescending(x => x.Value)
            .Select(x => new { AppName = x.Key, TotalSeconds = x.Value, Category = string.Empty });

        db.ActivityDailySummaries.Add(new ActivityDailySummary
        {
            Id = SeedId($"activity-summary:{employeeId:N}:{date:yyyy-MM-dd}"),
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            Date = date,
            TotalActiveMinutes = activeMinutes,
            TotalIdleMinutes = idleMinutes,
            TotalMeetingMinutes = 40,
            ActivePercentage = decimal.Round(activeMinutes * 100m / totalMinutes, 2),
            ProductiveAppMinutes = Math.Max(0, activeMinutes - 80),
            PersonalAppMinutes = 0,
            UnknownAppMinutes = 40,
            FocusMinutes = 180,
            ActivityScore = decimal.Round(78m + employeeIndex % 12, 2),
            DataCoveragePercentage = 100m,
            TopAppsJson = JsonSerializer.Serialize(topApps),
            IntensityAvg = decimal.Round(intensityTotal / 12m, 2),
            KeyboardTotal = keyboardTotal,
            MouseTotal = mouseTotal,
            DocumentTimeMinutes = 90,
            DeepFocusSessionsCount = 3,
            DataSource = "agent_windows",
            CreatedAt = At(date, 18, 10),
            UpdatedAt = At(date, 18, 10)
        });
        summaryKeys.Add(summaryKey);
    }

    private static void SeedVerificationAttempts(
        ApplicationDbContext db,
        Guid employeeId,
        DateOnly date,
        bool verificationAlertDay,
        HashSet<Guid> existingFaceAttemptIds)
    {
        if (verificationAlertDay)
        {
            AddFaceAttempt(
                db,
                existingFaceAttemptIds,
                employeeId,
                date,
                "clock_in-failed",
                "clock_in",
                FaceVerificationAttempt.OutcomeFailed,
                "not_matched",
                61f,
                At(date, 8, 58));
            AddFaceAttempt(
                db,
                existingFaceAttemptIds,
                employeeId,
                date,
                "clock_in-overridden",
                "clock_in",
                FaceVerificationAttempt.OutcomeOverridden,
                "retry_limit_reached",
                64f,
                At(date, 9, 1));
        }
        else
        {
            AddFaceAttempt(
                db,
                existingFaceAttemptIds,
                employeeId,
                date,
                "clock_in",
                "clock_in",
                FaceVerificationAttempt.OutcomePassed,
                null,
                96f,
                At(date, 8, 58));
        }

        AddFaceAttempt(
            db,
            existingFaceAttemptIds,
            employeeId,
            date,
            "clock_out",
            "clock_out",
            FaceVerificationAttempt.OutcomePassed,
            null,
            95f,
            At(date, 18, 1));
    }

    private static void AddFaceAttempt(
        ApplicationDbContext db,
        HashSet<Guid> existingIds,
        Guid employeeId,
        DateOnly date,
        string key,
        string purpose,
        string outcome,
        string? failureReason,
        float similarity,
        DateTimeOffset createdAt)
    {
        var id = SeedId($"face:{employeeId:N}:{date:yyyy-MM-dd}:{key}");
        if (!existingIds.Add(id))
            return;

        db.FaceVerificationAttempts.Add(new FaceVerificationAttempt
        {
            Id = id,
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            Purpose = purpose,
            Outcome = outcome,
            FailureReason = failureReason,
            SimilarityScore = similarity,
            CreatedAt = createdAt
        });
    }

    private static void SeedVerificationAlert(
        ApplicationDbContext db,
        Guid employeeId,
        int employeeIndex,
        DateOnly date,
        HashSet<Guid> existingExceptionIds,
        Dictionary<Guid, Notification> existingNotifications)
    {
        var detectedAt = At(date, 9, 2);
        var exceptionId = SeedId($"verification-exception:{employeeId:N}:{date:yyyy-MM-dd}");
        if (existingExceptionIds.Add(exceptionId))
        {
            var resolved = employeeIndex % 4 != 0;
            db.Exceptions.Add(new MonitoringException
            {
                Id = exceptionId,
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Type = ExceptionType.IdentityAnomaly,
                Status = resolved ? ExceptionStatus.Resolved : ExceptionStatus.Open,
                Title = "Attendance identity verification requires review",
                Description = "Face verification retries were exhausted during clock-in.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    purpose = "clock_in",
                    outcome = "overridden",
                    similarityScore = 64
                }),
                DetectedAt = detectedAt,
                ResolvedAt = resolved ? detectedAt.AddHours(2) : null,
                ResolvedById = resolved ? DapiOwnerUserId : null,
                ResolutionNote = resolved ? "Reviewed against the employee profile and confirmed." : null
            });
        }

        var notificationId = SeedId($"monitoring-alert:{employeeId:N}:{date:yyyy-MM-dd}");
        if (!existingNotifications.TryGetValue(notificationId, out var notification))
        {
            notification = new Notification
            {
                Id = notificationId,
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                CreatedAt = detectedAt,
                DeliveredToTrayAt = detectedAt.AddMinutes(1),
                ReadAt = employeeIndex % 3 == 0 ? null : detectedAt.AddHours(1)
            };
            db.MonitoringNotifications.Add(notification);
            existingNotifications.Add(notificationId, notification);
        }

        notification.Type = NotificationType.IdentityVerificationAlert;
        notification.Title = "Attendance identity verification requires review";
        notification.Message = $"A {MonthLabel(date)} attendance verification event requires manager review.";
        notification.MetadataJson = JsonSerializer.Serialize(new
        {
            reason = "attendance_identity_verification",
            exceptionId
        });
    }

    private static async Task SeedLeaveHistoryAsync(
        ApplicationDbContext db,
        Guid employeeId,
        LeavePlan plan,
        HashSet<Guid> existingRequestIds,
        HashSet<Guid> existingApproverIds,
        HashSet<Guid> existingAllocationIds,
        HashSet<Guid> existingAuditIds,
        CancellationToken ct)
    {
        var requestId = SeedId($"leave-request:{employeeId:N}:{plan.Date:yyyy-MM-dd}");
        var requestIsNew = existingRequestIds.Add(requestId);
        var submittedAt = At(plan.Date.AddDays(-5), 10, 0);
        var decidedAt = submittedAt.AddDays(1);

        if (requestIsNew)
        {
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = requestId,
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                LeaveTypeId = plan.LeaveTypeId,
                StartAt = At(plan.Date, 9, 0),
                EndAt = At(plan.Date, 18, 0),
                TotalHours = 8m,
                PaidHours = 8m,
                UnpaidHours = 0m,
                Reason = $"{MonthLabel(plan.Date)} Dapi demo leave history",
                Status = plan.Status,
                ApprovedBy = plan.Status == LeaveRequestStatuses.Approved ? plan.ApproverEmployeeId : null,
                ApprovedAt = plan.Status == LeaveRequestStatuses.Approved ? decidedAt : null,
                ConflictSnapshotJson = "{}",
                NoticePeriodMissed = false,
                CreatedAt = submittedAt,
                UpdatedAt = plan.Status == LeaveRequestStatuses.Pending ? submittedAt : decidedAt
            });
        }

        var approverRowId = SeedId($"leave-approver:{requestId:N}");
        if (!existingApproverIds.Add(approverRowId))
        {
            await RerouteIfPendingAsync(db, requestId, approverRowId, plan.ApproverEmployeeId, ct);
        }
        else
        {
            var approverStatus = plan.Status switch
            {
                LeaveRequestStatuses.Approved => LeaveRequestApproverStatuses.Approved,
                LeaveRequestStatuses.Rejected => LeaveRequestApproverStatuses.Rejected,
                _ => LeaveRequestApproverStatuses.Pending
            };
            db.LeaveRequestApprovers.Add(new LeaveRequestApprover
            {
                Id = approverRowId,
                TenantId = DapiTenantId,
                LeaveRequestId = requestId,
                ApproverEmployeeId = plan.ApproverEmployeeId,
                SequenceOrder = 1,
                Status = approverStatus,
                Comment = approverStatus == LeaveRequestApproverStatuses.Approved
                    ? $"Approved for the {MonthLabel(plan.Date)} demo history."
                    : approverStatus == LeaveRequestApproverStatuses.Rejected
                        ? "Rejected to demonstrate approval history."
                        : null,
                DecidedAt = approverStatus == LeaveRequestApproverStatuses.Pending ? null : decidedAt
            });
        }

        var allocationId = SeedId($"leave-allocation:{requestId:N}");
        if (existingAllocationIds.Add(allocationId))
        {
            db.LeaveRequestDayAllocations.Add(new LeaveRequestDayAllocation
            {
                Id = allocationId,
                TenantId = DapiTenantId,
                LeaveRequestId = requestId,
                LeaveDate = plan.Date,
                HoursUnit = 8m,
                PaidHoursUnit = 8m,
                UnpaidHoursUnit = 0m,
                Status = LeaveRequestDayAllocationStatuses.Active,
                CreatedAt = submittedAt
            });
        }

        if (!requestIsNew || plan.Status == LeaveRequestStatuses.Rejected)
            return;

        var entitlement = await db.LeaveEntitlements.FirstOrDefaultAsync(
            e => e.TenantId == DapiTenantId
                && e.EmployeeId == employeeId
                && e.LeaveTypeId == plan.LeaveTypeId
                && e.Year == plan.Date.Year,
            ct);
        if (entitlement is null)
            return;

        if (plan.Status == LeaveRequestStatuses.Pending)
        {
            entitlement.PendingHours += 8m;
            entitlement.UpdatedAt = submittedAt;
            return;
        }

        entitlement.UsedHours += 8m;
        entitlement.UpdatedAt = decidedAt;
        var auditId = SeedId($"leave-deduction:{requestId:N}");
        if (existingAuditIds.Add(auditId))
        {
            db.LeaveBalanceAudits.Add(new LeaveBalanceAudit
            {
                Id = auditId,
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                LeaveTypeId = plan.LeaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Deduction,
                HoursChanged = -8m,
                BalanceAfter = entitlement.TotalHours + entitlement.CarriedForwardHours - entitlement.UsedHours,
                Reason = $"Approved {MonthLabel(plan.Date)} Dapi demo leave",
                RelatedRequestId = requestId,
                CreatedAt = decidedAt,
                CreatedBy = DapiOwnerUserId
            });
        }
    }

    /// <summary>Who the leave route picks for this employee's leave on that day; null when it finds nobody.</summary>
    private static async Task<Guid?> ResolveApproverAsync(
        ILeaveApproverResolver? approvers, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        if (approvers is null)
            return null;
        var resolution = await approvers.ResolveAsync(DapiTenantId, employeeId, date, date, ct);
        return resolution.Approvers.OrderBy(a => a.SequenceOrder).Select(a => (Guid?)a.ApproverEmployeeId).FirstOrDefault();
    }

    /// <summary>
    /// A demo leave still waiting for its approver moves to whoever the leave route picks now (older seeds named the
    /// owner for everyone). Its case cards in 1-on-1 chats go too, so the inbox places it in the new approver's chat
    /// on their next load. Decided leaves keep their history.
    /// </summary>
    private static async Task RerouteIfPendingAsync(
        ApplicationDbContext db, Guid requestId, Guid approverRowId, Guid approverEmployeeId, CancellationToken ct)
    {
        var row = await db.LeaveRequestApprovers.FirstOrDefaultAsync(a => a.Id == approverRowId, ct);
        if (row is null || row.Status != LeaveRequestApproverStatuses.Pending || row.ApproverEmployeeId == approverEmployeeId)
            return;
        var requestPending = await db.LeaveRequests.AnyAsync(
            r => r.Id == requestId && r.Status == LeaveRequestStatuses.Pending, ct);
        if (!requestPending)
            return;

        row.ApproverEmployeeId = approverEmployeeId;
        row.DelegatedFromApproverId = null;

        var cardRef = $"\"id\":\"{requestId}\"";
        var directChatIds = db.Conversations
            .Where(c => c.TenantId == DapiTenantId && c.SubjectSource == ConversationSources.Direct)
            .Select(c => c.Id);
        var staleCards = await db.ConversationMessages
            .Where(m => m.TenantId == DapiTenantId
                && m.Kind == ConversationMessageKinds.Case
                && directChatIds.Contains(m.ConversationId)
                && m.SystemEventJson != null
                && m.SystemEventJson.Contains(cardRef))
            .ToListAsync(ct);
        if (staleCards.Count == 0)
            return;
        var staleIds = staleCards.Select(m => m.Id).ToList();
        var replies = await db.ConversationMessages
            .Where(m => m.ReplyToMessageId != null && staleIds.Contains(m.ReplyToMessageId.Value))
            .ToListAsync(ct);
        foreach (var reply in replies)
            reply.ReplyToMessageId = null;
        db.ConversationMessages.RemoveRange(staleCards);
    }

    private static async Task<HashSet<Guid>> ExistingIdsAsync(IQueryable<Guid> query, CancellationToken ct) =>
        (await query.ToListAsync(ct)).ToHashSet();

    private static Guid SeedId(string key) =>
        // Keep the original namespace for backward-compatible deterministic September IDs.
        WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-september-2026:{key}");

    private static string MonthLabel(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, ColomboOffset).ToUniversalTime();

    private static string HashTitle(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record LeavePlan(
        DateOnly Date,
        Guid LeaveTypeId,
        string Status,
        Guid ApproverEmployeeId);
}
