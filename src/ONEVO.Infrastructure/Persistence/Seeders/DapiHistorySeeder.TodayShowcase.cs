using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Today's team schedule showcase (2026-11-05 merged-widget design): five people whose today shows every case the widget
/// draws, laid out around the moment the API boots, so restarting the API just before a demo puts the red "now" line
/// where the story needs it.
///   Vithya Ganeshalingam - working, 6h of 8h planned: a finished task, a stand-up, a short break, a task in progress, one to come.
///   Nadesh Coomaraswamy  - in a meeting and over capacity (8h 45m of 8h): the meeting overlaps his task (a conflict).
///   Kiru Balachandran    - on a break, exactly at capacity (8h of 8h).
///   Saif Ahamed          - working but not planned: 1h 30m clocked on a task with no Planner slot (unscheduled work).
///   Lavanya Chandrasekaran - on approved leave today.
/// Every boot rebuilds these people's today (Planner blocks, task clocking, breaks, attendance and meeting times) from the
/// clock; the generic history skips them for today. Rows are keyed by the date, so each day keeps its own showcase.
/// </summary>
public sealed partial class DapiHistorySeeder
{
    private const string ShowcaseLeaveKey = "lavanya";

    /// <summary>The anchor ("now" in the story) is clamped to this local window, so every block stays on the day.</summary>
    private static readonly TimeSpan ShowcaseEarliestAnchor = new(13, 0, 0);
    private static readonly TimeSpan ShowcaseLatestAnchor = new(17, 30, 0);

    private enum ShowcaseState { Done, Active, ToDo }

    /// <summary>A task block: minutes from the anchor and length; Active = clocked in since the block began.</summary>
    private sealed record ShowcaseTask(string Key, string Title, int Start, int Minutes, ShowcaseState State);

    private sealed record ShowcaseMeeting(string Key, string Title, int Start, int Minutes);

    /// <summary>Unscheduled task clocking: a session with no Planner block.</summary>
    private sealed record ShowcaseOffPlan(string Key, string Title, int Start, int Minutes);

    /// <summary>A break: minutes from the anchor; a null length is a break still going on.</summary>
    private sealed record ShowcaseBreak(int Start, int? Minutes);

    private sealed record ShowcasePerson(
        Guid EmployeeId,
        IReadOnlyList<ShowcaseTask> Tasks,
        IReadOnlyList<ShowcaseMeeting> Meetings,
        IReadOnlyList<ShowcaseBreak> Breaks,
        IReadOnlyList<ShowcaseOffPlan> OffPlan);

    private static Guid DemoPerson(string key) => WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:employee:{key}");
    private static Guid OrgHire(string key) => WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-org:employee:{key}");

    /// <summary>The showcase cast (the leave person is handled by the leave plan). Offsets are minutes from the anchor.</summary>
    private static IReadOnlyList<ShowcasePerson> ShowcaseCast(int kiruMorningStart) =>
    [
        new(OrgHire("hrmgr"),
            [
                new("api-review", "API review", -270, 120, ShowcaseState.Done),
                new("leave-form", "Leave form", -60, 120, ShowcaseState.Active),
                new("tests", "Tests", 90, 75, ShowcaseState.ToDo),
            ],
            [new("standup", "Standup", -150, 45)],
            [new(-75, 15)],
            []),
        new(OrgHire("gm"),
            [
                new("payroll-export", "Payroll export", -330, 165, ShowcaseState.Done),
                new("team-week-view", "Team week view", -165, 180, ShowcaseState.Active),
                new("qa", "QA", 30, 120, ShowcaseState.ToDo),
            ],
            [new("client-sync", "Client sync", -30, 60)],
            [],
            []),
        new(DemoPerson("kiru"),
            [
                new("migration-script", "Migration script", kiruMorningStart, Math.Min(210, -kiruMorningStart - 60), ShowcaseState.Done),
                new("release-checklist", "Release checklist", 0, 210, ShowcaseState.ToDo),
            ],
            [new("sprint-plan", "Plan", kiruMorningStart - 75, 60)],
            [new(-45, null)],
            []),
        new(DemoPerson("saif"),
            [],
            [],
            [],
            [new("bug-triage", "Bug triage", -240, 90)]),
    ];

    /// <summary>The employees whose today the showcase owns (the generic history leaves their today alone).</summary>
    private static HashSet<Guid> ShowcaseEmployeeIds() =>
        ShowcaseCast(-300).Select(p => p.EmployeeId).ToHashSet();

    /// <summary>The showcase leave: one approved day off today for the leave person (added to the pure leave plan).</summary>
    private static void AddShowcaseLeave(SeedContext ctx, List<LeavePlan> plans, IReadOnlyList<LeaveType> leaveTypes)
    {
        var employeeId = DemoPerson(ShowcaseLeaveKey);
        if (leaveTypes.Count == 0 || !IsWorkday(ctx.Window.Today) || ctx.Employees.All(e => e.Id != employeeId))
            return;
        plans.RemoveAll(p => p.EmployeeId == employeeId && p.Date == ctx.Window.Today);
        plans.Add(new LeavePlan(employeeId, ctx.Window.Today, leaveTypes[0].Id, LeaveRequestStatuses.Approved, "Family function in Jaffna"));
    }

    /// <summary>The story's "now": the boot time rounded down to 15 minutes, kept inside 13:00-17:30 local.</summary>
    private static DateTimeOffset ShowcaseAnchor(SeedContext ctx)
    {
        var local = ctx.Now.ToOffset(ColomboOffset);
        var time = TimeSpan.FromMinutes(Math.Floor(local.TimeOfDay.TotalMinutes / 15) * 15);
        if (time < ShowcaseEarliestAnchor)
            time = ShowcaseEarliestAnchor;
        if (time > ShowcaseLatestAnchor)
            time = ShowcaseLatestAnchor;
        return At(ctx.Window.Today, time.Hours, time.Minutes);
    }

    private static async Task SeedTodayShowcaseAsync(SeedContext ctx, CancellationToken ct)
    {
        var today = ctx.Window.Today;
        if (!IsWorkday(today))
            return;
        var db = ctx.Db;
        var anchor = ShowcaseAnchor(ctx);
        // Kiru's morning task starts after the 09:15 team stand-up, so the generic stand-up never overlaps it.
        var standupEnd = At(today, 9, 30);
        var kiruMorningStart = Math.Max(-300, (int)(standupEnd - anchor).TotalMinutes);
        var cast = ShowcaseCast(kiruMorningStart)
            .Where(p => ctx.Employees.Any(e => e.Id == p.EmployeeId))
            .ToList();
        if (cast.Count == 0)
            return;

        var projectId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:project:{OwnerProjectKey}");
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return;
        var statuses = await db.TaskStatuses
            .Where(s => s.TenantId == DapiTenantId && s.ProjectId == projectId && s.ObjectiveId == null)
            .ToDictionaryAsync(s => s.Name, s => s.Id, ct);
        if (!new[] { ToDo, InProcess, Done }.All(statuses.ContainsKey))
            return;
        var categoryId = await db.TaskCategories
            .Where(c => c.TenantId == DapiTenantId && c.ProjectId == projectId)
            .OrderBy(c => c.Name)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        var objectives = await db.Objectives
            .Where(o => o.TenantId == DapiTenantId && o.ProjectId == projectId && !o.IsDeleted)
            .Select(o => new { o.Id, o.ParentObjectiveId, o.Title })
            .ToListAsync(ct);
        var parents = objectives.Where(o => o.ParentObjectiveId != null).Select(o => o.ParentObjectiveId!.Value).ToHashSet();
        var leaf = objectives.Where(o => !parents.Contains(o.Id)).OrderBy(o => o.Title).FirstOrDefault();
        if (leaf is null || categoryId is null)
            return;

        var dayStart = At(today, 0, 0);
        var dayEnd = At(today.AddDays(1), 0, 0);
        var ids = cast.Select(p => p.EmployeeId).ToList();

        // Rebuild today from the clock: drop today's blocks, task clocking and breaks for the cast.
        await db.Set<PlannedTaskBlock>()
            .Where(b => b.TenantId == DapiTenantId && ids.Contains(b.EmployeeId) && b.StartAt >= dayStart && b.StartAt < dayEnd)
            .ExecuteDeleteAsync(ct);
        await db.TaskClockingSessions
            .Where(s => s.TenantId == DapiTenantId && ids.Contains(s.EmployeeId) && s.ClockInAt >= dayStart && s.ClockInAt < dayEnd)
            .ExecuteDeleteAsync(ct);
        await db.BreakRecords
            .Where(b => b.TenantId == DapiTenantId && ids.Contains(b.EmployeeId) && b.BreakStart >= dayStart && b.BreakStart < dayEnd)
            .ExecuteDeleteAsync(ct);

        DateTimeOffset T(int offset) => anchor.AddMinutes(offset);

        foreach (var person in cast)
        {
            var employee = ctx.Employees.First(e => e.Id == person.EmployeeId);
            var userId = employee.UserId == Guid.Empty ? DapiOwnerUserId : employee.UserId;
            var key = $"today-showcase:{today:yyyy-MM-dd}:{person.EmployeeId:N}";

            await UpsertShowcaseAttendanceAsync(db, person.EmployeeId, today, At(today, 9, 0), ct);
            foreach (var (b, i) in person.Breaks.Select((b, i) => (b, i)))
            {
                db.BreakRecords.Add(new BreakRecord
                {
                    Id = SeedId($"{key}:break:{i}"),
                    TenantId = DapiTenantId,
                    EmployeeId = person.EmployeeId,
                    BreakStart = T(b.Start),
                    BreakEnd = b.Minutes is { } m ? T(b.Start + m) : null,
                    BreakType = "short",
                    AutoDetected = false,
                    CreatedAt = T(b.Start),
                });
            }

            foreach (var t in person.Tasks)
            {
                var task = await EnsureShowcaseTaskAsync(ctx, project, leaf.Id, categoryId.Value, statuses, person.EmployeeId, userId,
                    $"{key}:task:{t.Key}", t.Title, t.State, T(t.Start), T(t.Start + t.Minutes), ct);
                db.Set<PlannedTaskBlock>().Add(new PlannedTaskBlock
                {
                    Id = SeedId($"{key}:block:{t.Key}"),
                    TenantId = DapiTenantId,
                    EmployeeId = person.EmployeeId,
                    TaskId = task.Id,
                    StartAt = T(t.Start),
                    EndAt = T(t.Start + t.Minutes),
                    CreatedAt = At(today, 8, 30),
                    CreatedById = userId,
                });
                if (t.State == ShowcaseState.ToDo)
                    continue;
                var clockIn = T(t.Start + 5);
                var clockOut = t.State == ShowcaseState.Done ? T(t.Start + t.Minutes - 10) : (DateTimeOffset?)null;
                db.TaskClockingSessions.Add(new TaskClockingSession
                {
                    Id = SeedId($"{key}:session:{t.Key}"),
                    TenantId = DapiTenantId,
                    TaskId = task.Id,
                    EmployeeId = person.EmployeeId,
                    ClockInAt = clockIn,
                    ClockOutAt = clockOut,
                    DurationMinutes = clockOut is { } end ? (int)(end - clockIn).TotalMinutes : null,
                    CreatedAt = clockIn,
                    CreatedById = userId,
                });
            }

            foreach (var o in person.OffPlan)
            {
                var task = await EnsureShowcaseTaskAsync(ctx, project, leaf.Id, categoryId.Value, statuses, person.EmployeeId, userId,
                    $"{key}:task:{o.Key}", o.Title, ShowcaseState.Active, T(o.Start), T(o.Start + o.Minutes), ct);
                db.TaskClockingSessions.Add(new TaskClockingSession
                {
                    Id = SeedId($"{key}:session:{o.Key}"),
                    TenantId = DapiTenantId,
                    TaskId = task.Id,
                    EmployeeId = person.EmployeeId,
                    ClockInAt = T(o.Start),
                    ClockOutAt = T(o.Start + o.Minutes),
                    DurationMinutes = o.Minutes,
                    CreatedAt = T(o.Start),
                    CreatedById = userId,
                });
            }
            await db.SaveChangesAsync(ct);
        }

        // Meetings last: they go through the app's meeting writer, so a failure there leaves the rest in place.
        if (ctx.Services is not null)
        {
            foreach (var person in cast)
            {
                var key = $"today-showcase:{today:yyyy-MM-dd}:{person.EmployeeId:N}";
                foreach (var m in person.Meetings)
                    await EnsureShowcaseMeetingAsync(ctx, project, leaf.Id, person.EmployeeId, $"{key}:meeting:{m.Key}", m.Title,
                        T(m.Start), T(m.Start + m.Minutes), ct);
            }
        }
    }

    /// <summary>Today's attendance for a showcase person: clocked in at the given time, still at work.</summary>
    private static async Task UpsertShowcaseAttendanceAsync(
        ApplicationDbContext db, Guid employeeId, DateOnly today, DateTimeOffset clockIn, CancellationToken ct)
    {
        var record = await db.AttendanceRecords.FirstOrDefaultAsync(
            a => a.TenantId == DapiTenantId && a.EmployeeId == employeeId && a.Date == today, ct);
        if (record is null)
        {
            record = new AttendanceRecord
            {
                Id = SeedId($"attendance:{employeeId:N}:{today:yyyy-MM-dd}"),
                TenantId = DapiTenantId,
                EmployeeId = employeeId,
                Date = today,
                CreatedAt = clockIn,
            };
            db.AttendanceRecords.Add(record);
        }
        record.ExpectedWorkingDay = true;
        record.WorkTimeType = AttendanceRecord.WorkTimeTypeFixed;
        record.ScheduledStart = new TimeOnly(9, 0);
        record.ScheduledEnd = new TimeOnly(18, 0);
        record.RequiredWorkMinutes = 480;
        record.ScheduleTimezone = ColomboZoneId;
        record.IsHoliday = false;
        record.ActualStart = clockIn;
        record.ActualEnd = null;
        record.WorkedMinutes = 0;
        record.BreakMinutes = 0;
        record.LateMinutes = 0;
        record.AttendanceSource = AttendanceRecord.SourceWeb;
        record.Status = AttendanceRecord.StatusOnTime;
        record.UpdatedAt = clockIn;
    }

    /// <summary>One showcase task, assigned to the person, its status set to the story's state for today.</summary>
    private static async Task<WorkTask> EnsureShowcaseTaskAsync(
        SeedContext ctx, Domain.Features.WorkManagement.Projects.Entities.Project project, Guid objectiveId, Guid categoryId,
        IReadOnlyDictionary<string, Guid> statuses, Guid employeeId, Guid userId, string key, string title, ShowcaseState state,
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var db = ctx.Db;
        var id = SeedId(key);
        var task = db.WorkTasks.Local.FirstOrDefault(t => t.Id == id) ?? await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        var today = ctx.Window.Today;
        if (task is null)
        {
            task = new WorkTask
            {
                Id = id,
                TenantId = DapiTenantId,
                ProjectId = project.Id,
                ObjectiveId = objectiveId,
                ShortId = $"{project.Identifier}-{project.NextTaskNumber++}",
                Title = title,
                Description = WebUtility.HtmlEncode($"{title} (today's plan)."),
                CategoryId = categoryId,
                Priority = "medium",
                StoryPoints = 3,
                EstimatedHours = Math.Round((decimal)(end - start).TotalHours * 2, MidpointRounding.AwayFromZero) / 2,
                CreatedAt = At(today, 8, 0),
                CreatedById = userId,
            };
            db.WorkTasks.Add(task);
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = SeedId($"{key}:assignment"),
                TaskId = id,
                UserId = userId,
                EmployeeId = employeeId,
                AssignedById = employeeId, // an employee id, like the application writes (task 39b)
                AssignedAt = At(today, 8, 0),
            });
        }

        task.Title = title;
        task.DueDate = today;
        task.StartedAt = state == ShowcaseState.ToDo ? null : start;
        task.StatusId = statuses[state switch { ShowcaseState.Done => Done, ShowcaseState.Active => InProcess, _ => ToDo }];
        task.ProgressPercent = state switch { ShowcaseState.Done => 100, ShowcaseState.Active => 40, _ => 0 };
        task.CompletedAt = state == ShowcaseState.Done ? end.AddMinutes(-10) : null;
        task.CompletedHours = state == ShowcaseState.Done ? task.EstimatedHours ?? 0m : 0m;
        task.UpdatedAt = task.CompletedAt ?? task.StartedAt ?? task.CreatedAt;
        return task;
    }

    /// <summary>A one-off meeting organized by the person, materialized through the app's writer, then moved to the story's time.</summary>
    private static async Task EnsureShowcaseMeetingAsync(
        SeedContext ctx, Domain.Features.WorkManagement.Projects.Entities.Project project, Guid objectiveId, Guid organizerId,
        string key, string title, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var db = ctx.Db;
        var services = ctx.Services!;
        var writer = services.GetRequiredService<IMeetingOccurrenceWriter>();
        var materializer = services.GetRequiredService<IMeetingOccurrenceMaterializer>();
        var meetings = services.GetRequiredService<IMeetingRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var organizerUserId = ctx.Employees.First(e => e.Id == organizerId).UserId;
        if (organizerUserId == Guid.Empty)
            organizerUserId = DapiOwnerUserId;

        var seriesId = SeedId($"{key}:series");
        var series = await db.MeetingSeries.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            series = new MeetingSeries
            {
                Id = seriesId,
                TenantId = DapiTenantId,
                ProjectId = project.Id,
                ObjectiveId = objectiveId,
                OrganizerEmployeeId = organizerId,
                Title = title,
                Description = "Today's plan.",
                Location = "Teams",
                IsPrivate = false,
                DtStart = start,
                DurationMinutes = (int)(end - start).TotalMinutes,
                IsAllDay = false,
                Timezone = ColomboZoneId,
                RecurrenceRule = null,
                CreationPath = MeetingCreationPaths.Direct,
                CreatedAt = At(ctx.Window.Today, 8, 0),
                CreatedById = organizerUserId,
            };
            db.MeetingSeries.Add(series);
            await db.SaveChangesAsync(ct);
        }

        var slot = series.DtStart;
        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await writer.LockSeriesAsync(DapiTenantId, series.Id, innerCt);
            if (await meetings.GetDetailsAsync(DapiTenantId, series.Id, slot, innerCt) is null)
                await materializer.EnsureAsync(DapiTenantId, series, slot, organizerUserId, innerCt);
            return true;
        }, ct);
        db.ChangeTracker.Clear();

        // The occurrence keeps its key (OriginalStart); only its time follows today's anchor.
        await db.MeetingDetails
            .Where(d => d.TenantId == DapiTenantId && d.SeriesId == seriesId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(d => d.StartAt, start)
                .SetProperty(d => d.EndAt, end)
                .SetProperty(d => d.IsDetached, true)
                .SetProperty(d => d.CancelledAt, (DateTimeOffset?)null), ct);
    }
}
