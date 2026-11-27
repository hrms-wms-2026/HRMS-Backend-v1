using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiPerformanceBaselineSeeder
{
    private const string ColomboZoneId = "Asia/Colombo";
    private static readonly TimeSpan ColomboOffset = TimeSpan.FromMinutes(330);

    /// <summary>
    /// One attendance record per employee per day for the last <see cref="AttendanceDays"/> days. The baseline people
    /// get every day up to yesterday. The original Dapi people already have the rolling history from
    /// <see cref="DapiHistorySeeder"/>, so they only get the days before its window; days that already have a record
    /// are left alone either way.
    /// </summary>
    private static async Task<int> SeedAttendanceAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(ColomboOffset).DateTime);
        var from = today.AddDays(-AttendanceDays);
        var historyFrom = today.AddDays(-DapiHistorySeeder.HistoryDays);

        var employees = await db.Employees
            .Where(e => e.TenantId == DapiTenantId && e.LegalEntityId == DapiLegalEntityId
                && !e.IsDeleted && e.TerminationDate == null)
            .Select(e => new { e.Id, e.EmployeeNumber })
            .ToListAsync(ct);
        var existing = (await db.AttendanceRecords
                .Where(a => a.TenantId == DapiTenantId && a.Date >= from && a.Date < today)
                .Select(a => new { a.EmployeeId, a.Date })
                .ToListAsync(ct))
            .Select(a => (a.EmployeeId, a.Date))
            .ToHashSet();
        var mode = await db.TimeAttendanceWorkModes
            .Where(w => w.TenantId == DapiTenantId && w.LegalEntityId == DapiLegalEntityId
                && w.Name == DapiOrgStructureSeeder.DefaultWorkModeName && w.IsActive)
            .Select(w => new { w.Id, w.Name })
            .FirstOrDefaultAsync(ct);

        var added = 0;
        var pending = 0;
        for (var e = 0; e < employees.Count; e++)
        {
            var employee = employees[e];
            var isBaseline = employee.EmployeeNumber.StartsWith(EmployeeNumberPrefix, StringComparison.Ordinal);
            var until = isBaseline ? today : historyFrom;

            for (var date = from; date < until; date = date.AddDays(1))
            {
                if (!existing.Add((employee.Id, date)))
                    continue;

                var isWorkday = date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
                var roll = Pick((e * 1_000) + date.DayNumber, 100);
                var onLeave = isWorkday && roll >= 96;
                var worked = isWorkday && !onLeave;
                var late = worked && roll < 12 ? 10 + (roll * 3 % 35) : 0;
                var shortHours = worked && roll >= 90;
                var clockIn = At(date, 9, late - (roll % 10));
                var clockOut = shortHours ? At(date, 16, 20 + (roll % 30)) : At(date, 18, roll % 40);
                var breakMinutes = 45 + (roll % 20);
                var status = !isWorkday ? AttendanceRecord.StatusNonWorkingDay
                    : onLeave ? AttendanceRecord.StatusOnTimeOff
                    : shortHours ? AttendanceRecord.StatusShortHours
                    : late > 0 ? AttendanceRecord.StatusLate
                    : AttendanceRecord.StatusNormal;
                var written = worked ? clockOut.AddMinutes(5) : At(date, 18, 5);

                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    Id = Id($"attendance:{employee.Id:N}:{date:yyyy-MM-dd}"),
                    TenantId = DapiTenantId,
                    EmployeeId = employee.Id,
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
                    ActualStart = worked ? clockIn : null,
                    ActualEnd = worked ? clockOut : null,
                    WorkedMinutes = worked ? Math.Max(0, (int)(clockOut - clockIn).TotalMinutes - breakMinutes) : 0,
                    BreakMinutes = worked ? breakMinutes : 0,
                    LateMinutes = worked ? late : null,
                    AttendanceSource = "agent",
                    Status = status,
                    CreatedAt = written,
                    UpdatedAt = written
                });
                added++;
                if (++pending >= SaveBatch)
                {
                    await SaveBatchAsync(db, ct);
                    pending = 0;
                }
            }
        }

        if (pending > 0)
            await SaveBatchAsync(db, ct);
        return added;
    }

    private static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, ColomboOffset)
            .AddHours(hour).AddMinutes(minute).ToUniversalTime();
}
