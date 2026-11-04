using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiHistorySeeder
{
    /// <summary>One person's seeded workday: when they came and went, and their breaks.</summary>
    private sealed record DayShape(
        DateTimeOffset ClockIn, DateTimeOffset ClockOut, int LateMinutes, bool ShortHours, bool OverBreak,
        IReadOnlyList<(string Key, string Type, DateTimeOffset Start, DateTimeOffset End)> Breaks)
    {
        public int BreakMinutes => Breaks.Sum(b => (int)(b.End - b.Start).TotalMinutes);
        public int WorkedMinutes => Math.Max(0, (int)(ClockOut - ClockIn).TotalMinutes - BreakMinutes);
    }

    private static DayShape ShapeOf(Guid employeeId, int index, DateOnly date)
    {
        var key = $"{employeeId:N}:{date:yyyy-MM-dd}";
        var roll = Pick($"day:{key}", 100);
        var lateMinutes = roll < 12 ? 10 + Pick($"late:{key}", 35) : 0;
        var earlyMinutes = roll >= 12 && roll < 40 ? Pick($"early:{key}", 15) : 0;
        var shortHours = roll >= 92;
        var overBreak = roll >= 86 && roll < 92;
        var clockIn = At(date, 9, lateMinutes - earlyMinutes);
        var clockOut = shortHours
            ? At(date, 16, 10 + Pick($"short:{key}", 30))
            : At(date, 18, Pick($"out:{key}", 40));

        var lunchStart = At(date, 12, 45 + Pick($"lunch:{key}", 30));
        var lunchLength = overBreak ? 80 + Pick($"over:{key}", 20) : 45 + Pick($"len:{key}", 16);
        var breaks = new List<(string, string, DateTimeOffset, DateTimeOffset)>
        {
            ("lunch", "lunch", lunchStart, lunchStart.AddMinutes(lunchLength))
        };
        // A short tea break most afternoons, a morning one some days.
        if (Pick($"tea:{key}", 4) != 0)
        {
            var tea = At(date, 15, 30 + Pick($"teat:{key}", 30));
            breaks.Add(("tea", "short", tea, tea.AddMinutes(10 + Pick($"teal:{key}", 6))));
        }
        if ((index + date.Day) % 5 == 0)
        {
            var morning = At(date, 10, 50 + Pick($"am:{key}", 15));
            breaks.Add(("morning", "short", morning, morning.AddMinutes(10)));
        }
        return new DayShape(clockIn, clockOut, lateMinutes, shortHours, overBreak, breaks);
    }

    /// <param name="plannedLeave">Approved leave days per employee (from the leave plan): time-off attendance, no activity.</param>
    private static async Task SeedAttendanceAndMonitoringAsync(
        SeedContext ctx, IReadOnlyDictionary<Guid, HashSet<DateOnly>> plannedLeave, CancellationToken ct)
    {
        var db = ctx.Db;
        var from = ctx.Window.From;
        var today = ctx.Window.Today;
        var rangeStart = At(from, 0, 0);
        var rangeEnd = At(today.AddDays(1), 0, 0);

        var attendanceKeys = (await db.AttendanceRecords
                .Where(x => x.TenantId == DapiTenantId && x.Date >= from && x.Date <= today)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync(ct))
            .Select(x => (x.EmployeeId, x.Date))
            .ToHashSet();
        var presenceKeys = (await db.PresenceSessions
                .Where(x => x.TenantId == DapiTenantId && x.Date >= from && x.Date <= today)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync(ct))
            .Select(x => (x.EmployeeId, x.Date))
            .ToHashSet();
        var summaryKeys = (await db.ActivityDailySummaries
                .Where(x => x.TenantId == DapiTenantId && x.Date >= from && x.Date <= today)
                .Select(x => new { x.EmployeeId, x.Date })
                .ToListAsync(ct))
            .Select(x => (x.EmployeeId, x.Date))
            .ToHashSet();
        var ids = new ExistingRows(
            await ExistingIdsAsync(db.BreakRecords.Where(x => x.TenantId == DapiTenantId
                && x.BreakStart >= rangeStart && x.BreakStart < rangeEnd).Select(x => x.Id), ct),
            await ExistingIdsAsync(db.ActivitySnapshots.Where(x => x.TenantId == DapiTenantId
                && x.CapturedAt >= rangeStart && x.CapturedAt < rangeEnd).Select(x => x.Id), ct),
            await ExistingIdsAsync(db.AppUsageSnapshots.Where(x => x.TenantId == DapiTenantId
                && x.CapturedAt >= rangeStart && x.CapturedAt < rangeEnd).Select(x => x.Id), ct),
            await ExistingIdsAsync(db.DeviceStateSnapshots.Where(x => x.TenantId == DapiTenantId
                && x.CapturedAt >= rangeStart && x.CapturedAt < rangeEnd).Select(x => x.Id), ct),
            await ExistingIdsAsync(db.FaceVerificationAttempts.Where(x => x.TenantId == DapiTenantId
                && x.CreatedAt >= rangeStart && x.CreatedAt < rangeEnd).Select(x => x.Id), ct));

        var workMode = await db.TimeAttendanceWorkModes
            .Where(w => w.TenantId == DapiTenantId && w.LegalEntityId == DapiLegalEntityId && w.IsActive)
            .OrderBy(w => w.DisplayOrder)
            .ToListAsync(ct);
        var onsite = workMode.FirstOrDefault(m => m.Name.Contains("onsite", StringComparison.OrdinalIgnoreCase))
            ?? workMode.FirstOrDefault();

        var allDates = ctx.Window.PastDays().Append(today).ToList();
        if (ctx.ClockInTester is not null)
            await RemoveSeededAttendanceAsync(db, ctx.ClockInTester.Id, allDates, ct);

        for (var index = 0; index < ctx.Employees.Count; index++)
        {
            var employee = ctx.Employees[index];
            var seedsAttendance = employee.Id != ctx.ClockInTester?.Id;
            var leaveDays = plannedLeave.GetValueOrDefault(employee.Id) ?? [];

            foreach (var date in ctx.Window.PastDays())
            {
                var isWorkday = IsWorkday(date);
                var onLeave = leaveDays.Contains(date);
                var shape = ShapeOf(employee.Id, index, date);

                if (seedsAttendance)
                    SeedAttendanceDay(db, employee.Id, onsite, date, isWorkday, onLeave, shape, attendanceKeys, presenceKeys, ids);

                if (isWorkday && !onLeave)
                {
                    SeedMonitoringDay(db, employee.Id, index, date, shape, summaryKeys, ids);
                    if (seedsAttendance)
                        SeedFaceChecks(db, employee.Id, index, date, ids);
                }
            }

            if (seedsAttendance && IsWorkday(today) && !leaveDays.Contains(today))
                SeedToday(db, ctx, employee.Id, index, onsite, attendanceKeys, ids);

            if ((index + 1) % 4 == 0)
                await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed record ExistingRows(
        HashSet<Guid> Breaks, HashSet<Guid> Activity, HashSet<Guid> AppUsage, HashSet<Guid> DeviceState, HashSet<Guid> Face);

    private static void SeedAttendanceDay(
        ApplicationDbContext db, Guid employeeId, WorkMode? mode, DateOnly date, bool isWorkday, bool onLeave, DayShape shape,
        HashSet<(Guid, DateOnly)> attendanceKeys, HashSet<(Guid, DateOnly)> presenceKeys, ExistingRows ids)
    {
        var key = (employeeId, date);
        var worked = isWorkday && !onLeave;
        if (attendanceKeys.Add(key))
        {
            var status = !isWorkday ? AttendanceRecord.StatusNonWorkingDay
                : onLeave ? AttendanceRecord.StatusOnTimeOff
                : shape.ShortHours ? AttendanceRecord.StatusShortHours
                : shape.OverBreak ? AttendanceRecord.StatusOverBreak
                : shape.LateMinutes > 0 ? AttendanceRecord.StatusLate
                : AttendanceRecord.StatusNormal;

            db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = SeedId($"attendance:{employeeId:N}:{date:yyyy-MM-dd}"),
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Date = date,
                ExpectedWorkingDay = isWorkday,
                WorkTimeType = AttendanceRecord.WorkTimeTypeFixed,
                ScheduledStart = isWorkday ? new TimeOnly(9, 0) : null,
                ScheduledEnd = isWorkday ? new TimeOnly(18, 0) : null,
                RequiredWorkMinutes = isWorkday ? 480 : null,
                ExpectedWorkModeId = isWorkday ? mode?.Id : null,
                ExpectedWorkModeName = isWorkday ? mode?.Name : null,
                ScheduleTimezone = ColomboZoneId,
                IsHoliday = false,
                ActualStart = worked ? shape.ClockIn : null,
                ActualEnd = worked ? shape.ClockOut : null,
                WorkedMinutes = worked ? shape.WorkedMinutes : 0,
                BreakMinutes = worked ? shape.BreakMinutes : 0,
                LateMinutes = worked ? shape.LateMinutes : null,
                AttendanceSource = "agent",
                Status = status,
                CreatedAt = worked ? shape.ClockOut.AddMinutes(5) : At(date, 18, 5),
                UpdatedAt = worked ? shape.ClockOut.AddMinutes(5) : At(date, 18, 5)
            });
        }

        if (!worked)
            return;

        if (presenceKeys.Add(key))
        {
            db.PresenceSessions.Add(new PresenceSession
            {
                Id = SeedId($"presence:{employeeId:N}:{date:yyyy-MM-dd}"),
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Date = date,
                FirstSeenAt = shape.ClockIn,
                LastSeenAt = shape.ClockOut,
                TotalPresentMinutes = shape.WorkedMinutes,
                TotalBreakMinutes = shape.BreakMinutes,
                Source = "agent",
                Status = "closed",
                CreatedAt = shape.ClockIn,
                UpdatedAt = shape.ClockOut
            });
        }

        foreach (var (breakKey, type, start, end) in shape.Breaks)
            AddBreak(db, ids, employeeId, date, breakKey, type, start, end);
    }

    /// <summary>Today, filled up to now: clocked in (still working), the breaks already taken.</summary>
    private static void SeedToday(
        ApplicationDbContext db, SeedContext ctx, Guid employeeId, int index, WorkMode? mode,
        HashSet<(Guid, DateOnly)> attendanceKeys, ExistingRows ids)
    {
        var today = ctx.Window.Today;
        var shape = ShapeOf(employeeId, index, today);
        if (shape.ClockIn > ctx.Now)
            return;

        if (attendanceKeys.Add((employeeId, today)))
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = SeedId($"attendance:{employeeId:N}:{today:yyyy-MM-dd}"),
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Date = today,
                ExpectedWorkingDay = true,
                WorkTimeType = AttendanceRecord.WorkTimeTypeFixed,
                ScheduledStart = new TimeOnly(9, 0),
                ScheduledEnd = new TimeOnly(18, 0),
                RequiredWorkMinutes = 480,
                ExpectedWorkModeId = mode?.Id,
                ExpectedWorkModeName = mode?.Name,
                ScheduleTimezone = ColomboZoneId,
                IsHoliday = false,
                ActualStart = shape.ClockIn,
                ActualEnd = null,
                WorkedMinutes = 0,
                BreakMinutes = 0,
                LateMinutes = shape.LateMinutes,
                AttendanceSource = AttendanceRecord.SourceWeb,
                Status = shape.LateMinutes > 0 ? AttendanceRecord.StatusLate : AttendanceRecord.StatusOnTime,
                CreatedAt = shape.ClockIn,
                UpdatedAt = shape.ClockIn
            });
        }

        foreach (var (breakKey, type, start, end) in shape.Breaks.Where(b => b.End <= ctx.Now))
            AddBreak(db, ids, employeeId, today, breakKey, type, start, end);
        SeedFaceChecks(db, employeeId, index, today, ids, clockOut: false);
    }

    private static void AddBreak(
        ApplicationDbContext db, ExistingRows ids, Guid employeeId, DateOnly date, string key, string type,
        DateTimeOffset start, DateTimeOffset end)
    {
        // The lunch break keeps the id the fixed-month version gave it.
        var id = key == "lunch"
            ? SeedId($"break:{employeeId:N}:{date:yyyy-MM-dd}")
            : SeedId($"break:{employeeId:N}:{date:yyyy-MM-dd}:{key}");
        if (!ids.Breaks.Add(id))
            return;
        db.BreakRecords.Add(new BreakRecord
        {
            Id = id,
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            BreakStart = start,
            BreakEnd = end,
            BreakType = type,
            AutoDetected = type != "lunch",
            CreatedAt = start
        });
    }

    private static void SeedMonitoringDay(
        ApplicationDbContext db, Guid employeeId, int employeeIndex, DateOnly date, DayShape shape,
        HashSet<(Guid, DateOnly)> summaryKeys, ExistingRows ids)
    {
        var deviceId = SeedId($"device:{employeeId:N}");
        var dayKey = $"{employeeId:N}:{date:yyyy-MM-dd}";
        // A few low-energy days a month, so the activity trends and low-activity cases have something to show.
        var lowDay = Pick($"low:{dayKey}", 100) < 8;
        var processTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int activeTotal = 0, idleTotal = 0, keyboardTotal = 0, mouseTotal = 0, meetingSeconds = 0;
        decimal intensityTotal = 0;
        var slots = Math.Max(6, (int)((shape.ClockOut - shape.ClockIn).TotalMinutes / 40));

        for (var slot = 0; slot < slots; slot++)
        {
            var capturedAt = shape.ClockIn.AddMinutes(20 + slot * 40);
            if (shape.Breaks.Any(b => capturedAt >= b.Start && capturedAt < b.End))
                continue;
            var app = SeedApplications[(employeeIndex + slot + Pick($"app:{dayKey}:{slot}", 3)) % SeedApplications.Length];
            var baseActive = lowDay ? 1_100 : 2_050;
            var activeSeconds = Math.Min(2_400, baseActive - Pick($"act:{dayKey}:{slot}", 420) + 180);
            var idleSeconds = 2_400 - activeSeconds;
            var keyboard = (lowDay ? 20 : 45) + Pick($"kb:{dayKey}:{slot}", 40);
            var mouse = (lowDay ? 40 : 85) + Pick($"ms:{dayKey}:{slot}", 60);
            var intensity = decimal.Round((keyboard + mouse) / 2.2m, 2);

            activeTotal += activeSeconds;
            idleTotal += idleSeconds;
            keyboardTotal += keyboard;
            mouseTotal += mouse;
            intensityTotal += intensity;
            if (app.Category == "meeting")
                meetingSeconds += activeSeconds;
            processTotals[app.ProcessName] = processTotals.GetValueOrDefault(app.ProcessName) + activeSeconds;

            var activityId = SeedId($"activity:{employeeId:N}:{date:yyyy-MM-dd}:{slot}");
            if (ids.Activity.Add(activityId))
            {
                db.ActivitySnapshots.Add(new ActivitySnapshot
                {
                    Id = activityId, TenantId = DapiTenantId, EmployeeId = employeeId, AgentDeviceId = deviceId,
                    CapturedAt = capturedAt, KeyboardEventsCount = keyboard, MouseEventsCount = mouse,
                    ActiveSeconds = activeSeconds, IdleSeconds = idleSeconds, IntensityScore = intensity,
                    ForegroundProcessName = app.ProcessName, CreatedAt = capturedAt
                });
            }

            var usageId = SeedId($"app-usage:{employeeId:N}:{date:yyyy-MM-dd}:{slot}");
            if (ids.AppUsage.Add(usageId))
            {
                db.AppUsageSnapshots.Add(new AppUsageSnapshot
                {
                    Id = usageId, TenantId = DapiTenantId, EmployeeId = employeeId, AgentDeviceId = deviceId,
                    CapturedAt = capturedAt, ProcessName = app.ProcessName,
                    WindowTitleHash = HashTitle($"dapi-demo:{app.ProcessName}:{employeeId:N}:{date:yyyy-MM-dd}:{slot}"),
                    CreatedAt = capturedAt
                });
            }

            var deviceStateId = SeedId($"device-state:{employeeId:N}:{date:yyyy-MM-dd}:{slot}");
            if (ids.DeviceState.Add(deviceStateId))
            {
                db.DeviceStateSnapshots.Add(new DeviceStateSnapshot
                {
                    Id = deviceStateId, TenantId = DapiTenantId, EmployeeId = employeeId, AgentDeviceId = deviceId,
                    CapturedAt = capturedAt, IdleSeconds = idleSeconds, IsIdle = idleSeconds >= 600,
                    Latitude = 6.9271, Longitude = 79.8612, AccuracyMeters = 18, CreatedAt = capturedAt
                });
            }
        }

        if (!summaryKeys.Add((employeeId, date)))
            return;

        var activeMinutes = activeTotal / 60;
        var idleMinutes = idleTotal / 60;
        var totalMinutes = Math.Max(1, activeMinutes + idleMinutes);
        var meetingMinutes = meetingSeconds / 60 + 15;
        var unknownMinutes = 15 + Pick($"unk:{dayKey}", 30);
        var personalMinutes = Pick($"pers:{dayKey}", 25);
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
            TotalMeetingMinutes = meetingMinutes,
            ActivePercentage = decimal.Round(activeMinutes * 100m / totalMinutes, 2),
            ProductiveAppMinutes = Math.Max(0, activeMinutes - meetingMinutes - unknownMinutes - personalMinutes),
            PersonalAppMinutes = personalMinutes,
            UnknownAppMinutes = unknownMinutes,
            FocusMinutes = lowDay ? 60 + Pick($"foc:{dayKey}", 40) : 150 + Pick($"foc:{dayKey}", 90),
            ActivityScore = decimal.Round((lowDay ? 48m : 72m) + Pick($"score:{dayKey}", 22), 2),
            DataCoveragePercentage = 100m,
            TopAppsJson = JsonSerializer.Serialize(topApps),
            IntensityAvg = decimal.Round(intensityTotal / Math.Max(1, slots), 2),
            KeyboardTotal = keyboardTotal,
            MouseTotal = mouseTotal,
            DocumentTimeMinutes = 40 + Pick($"doc:{dayKey}", 80),
            DeepFocusSessionsCount = lowDay ? 1 : 2 + Pick($"deep:{dayKey}", 3),
            DataSource = "agent_windows",
            CreatedAt = shape.ClockOut.AddMinutes(10),
            UpdatedAt = shape.ClockOut.AddMinutes(10)
        });
    }

    /// <summary>Clock-in and clock-out face checks; about one day a month needs retries and an override.</summary>
    private static void SeedFaceChecks(
        ApplicationDbContext db, Guid employeeId, int index, DateOnly date, ExistingRows ids, bool clockOut = true)
    {
        var shape = ShapeOf(employeeId, index, date);
        if (IsFailedCheckDay(employeeId, date))
        {
            AddFaceAttempt(db, ids.Face, employeeId, date, "clock_in-failed", "clock_in",
                FaceVerificationAttempt.OutcomeFailed, "not_matched", 61f, shape.ClockIn.AddMinutes(-2));
            AddFaceAttempt(db, ids.Face, employeeId, date, "clock_in-overridden", "clock_in",
                FaceVerificationAttempt.OutcomeOverridden, "retry_limit_reached", 64f, shape.ClockIn.AddMinutes(1));
        }
        else
        {
            AddFaceAttempt(db, ids.Face, employeeId, date, "clock_in", "clock_in",
                FaceVerificationAttempt.OutcomePassed, null, 92f + Pick($"sim:{employeeId:N}:{date}", 7), shape.ClockIn.AddMinutes(-2));
        }

        if (clockOut)
            AddFaceAttempt(db, ids.Face, employeeId, date, "clock_out", "clock_out",
                FaceVerificationAttempt.OutcomePassed, null, 95f, shape.ClockOut.AddMinutes(1));
    }

    private static bool IsFailedCheckDay(Guid employeeId, DateOnly date) =>
        IsWorkday(date) && Pick($"face-fail:{employeeId:N}:{date:yyyy-MM-dd}", 22) == 0;

    private static void AddFaceAttempt(
        ApplicationDbContext db, HashSet<Guid> existingIds, Guid employeeId, DateOnly date, string key, string purpose,
        string outcome, string? failureReason, float similarity, DateTimeOffset createdAt)
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

    /// <summary>Deletes only the rows this seeder wrote (deterministic ids) - attendance the person clocks live stays.</summary>
    private static async Task RemoveSeededAttendanceAsync(
        ApplicationDbContext db, Guid employeeId, IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var attendanceIds = dates.Select(d => SeedId($"attendance:{employeeId:N}:{d:yyyy-MM-dd}")).ToList();
        var presenceIds = dates.Select(d => SeedId($"presence:{employeeId:N}:{d:yyyy-MM-dd}")).ToList();
        string[] breakKeys = ["", ":tea", ":morning"];
        var breakIds = dates
            .SelectMany(d => breakKeys.Select(k => SeedId($"break:{employeeId:N}:{d:yyyy-MM-dd}{k}")))
            .ToList();
        string[] faceKeys = ["clock_in", "clock_in-failed", "clock_in-overridden", "clock_out"];
        var faceIds = dates
            .SelectMany(d => faceKeys.Select(k => SeedId($"face:{employeeId:N}:{d:yyyy-MM-dd}:{k}")))
            .ToList();
        await db.BreakRecords.Where(b => breakIds.Contains(b.Id)).ExecuteDeleteAsync(ct);
        await db.FaceVerificationAttempts.Where(f => faceIds.Contains(f.Id)).ExecuteDeleteAsync(ct);
        // A correction filed against a seeded day keeps that day (it points at the row).
        await db.PresenceSessions
            .Where(p => presenceIds.Contains(p.Id) && !db.AttendanceCorrections.Any(c => c.PresenceSessionId == p.Id))
            .ExecuteDeleteAsync(ct);
        await db.AttendanceRecords
            .Where(a => attendanceIds.Contains(a.Id) && !db.AttendanceCorrections.Any(c => c.AttendanceRecordId == a.Id))
            .ExecuteDeleteAsync(ct);
    }
}
