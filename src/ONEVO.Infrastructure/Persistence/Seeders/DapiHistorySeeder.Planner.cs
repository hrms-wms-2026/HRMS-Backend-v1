using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiHistorySeeder
{
    /// <summary>The owner's own work: tasks from this project are assigned to them so their planner and My Tasks
    /// are not empty in the demo.</summary>
    private const string OwnerProjectKey = "onexso";
    private const int OwnerTaskCount = 6;

    /// <summary>The day's planned slots (local time): morning focus, early afternoon, late afternoon.</summary>
    private static readonly (int StartHour, int StartMinute, int Minutes)[] BlockSlots =
    [
        (9, 30, 120),
        (14, 0, 90),
        (16, 0, 90)
    ];

    private static async Task SeedPlannerAsync(SeedContext ctx, IReadOnlyDictionary<Guid, HashSet<DateOnly>> leaveDays, CancellationToken ct)
    {
        var db = ctx.Db;
        await EnsureOwnerAssignmentsAsync(ctx, ct);

        var employeeIds = ctx.Employees.Select(e => e.Id).ToList();
        var assigned = await db.TaskAssignments
            .Where(a => employeeIds.Contains(a.EmployeeId))
            .Join(db.WorkTasks.Where(t => t.TenantId == DapiTenantId && !t.IsDeleted),
                a => a.TaskId, t => t.Id, (a, t) => new { a.EmployeeId, Task = t })
            .ToListAsync(ct);
        var tasksByEmployee = assigned
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Task).DistinctBy(t => t.Id).OrderBy(t => t.ShortId).ToList());
        // Tasks of the demo projects already have their dates (SeedWorkHistoryAsync); anything else assigned gets them here.
        var doneStatusIds = (await db.TaskStatuses
                .Where(s => s.TenantId == DapiTenantId && s.MarksTaskComplete)
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet();
        GiveTasksALifecycle(ctx, tasksByEmployee.Values.SelectMany(t => t).DistinctBy(t => t.Id), doneStatusIds);
        await db.SaveChangesAsync(ct);

        var rangeStart = At(ctx.Window.From, 0, 0);
        var rangeEnd = At(ctx.Window.Today.AddDays(1), 0, 0);
        var existingBlocks = await ExistingIdsAsync(db.Set<PlannedTaskBlock>()
            .Where(b => b.TenantId == DapiTenantId && b.StartAt >= rangeStart && b.StartAt < rangeEnd).Select(b => b.Id), ct);
        var existingSessions = await ExistingIdsAsync(db.TaskClockingSessions
            .Where(s => s.TenantId == DapiTenantId && s.ClockInAt >= rangeStart && s.ClockInAt < rangeEnd).Select(s => s.Id), ct);
        var days = ctx.Window.PastWorkdays().Append(ctx.Window.Today).Where(IsWorkday).ToList();
        var minutesByTask = new Dictionary<Guid, int>();

        foreach (var employee in ctx.Employees)
        {
            if (!tasksByEmployee.TryGetValue(employee.Id, out var tasks) || tasks.Count == 0)
                continue;
            var userId = employee.UserId == Guid.Empty ? DapiOwnerUserId : employee.UserId;
            var off = leaveDays.GetValueOrDefault(employee.Id) ?? [];

            for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
            {
                var date = days[dayIndex];
                if (off.Contains(date))
                    continue;
                var open = tasks.Where(t => PlannableOn(t, date)).ToList();
                if (open.Count == 0)
                    continue;

                for (var slot = 0; slot < BlockSlots.Length; slot++)
                {
                    var key = $"{employee.Id:N}:{date:yyyy-MM-dd}:{slot}";
                    // Most days are fully planned; some afternoons are left open.
                    if (slot > 0 && Pick($"plan:{key}", 7) == 0)
                        continue;
                    var task = open[(dayIndex + slot + Pick($"task:{employee.Id:N}:{dayIndex}", 2)) % open.Count];
                    var (hour, minute, length) = BlockSlots[slot];
                    var start = At(date, hour, minute);
                    var end = start.AddMinutes(length);

                    var blockId = SeedId($"planner-block:{key}");
                    if (existingBlocks.Add(blockId))
                    {
                        db.Set<PlannedTaskBlock>().Add(new PlannedTaskBlock
                        {
                            Id = blockId, TenantId = DapiTenantId, EmployeeId = employee.Id, TaskId = task.Id,
                            StartAt = start, EndAt = end, CreatedAt = At(date, 8, 45), CreatedById = userId
                        });
                    }

                    // Actuals: only for time that has passed. Most blocks are worked, a few skipped, some overrun.
                    if (end > ctx.Now)
                        continue;
                    var actual = Pick($"actual:{key}", 20);
                    if (actual == 0)
                        continue;
                    var clockIn = start.AddMinutes(Pick($"in:{key}", 12));
                    var worked = actual == 1 ? length + 30 : length - Pick($"len:{key}", 25);
                    var sessionId = SeedId($"planner-session:{key}");
                    if (existingSessions.Add(sessionId))
                    {
                        db.TaskClockingSessions.Add(new TaskClockingSession
                        {
                            Id = sessionId, TenantId = DapiTenantId, TaskId = task.Id, EmployeeId = employee.Id,
                            ClockInAt = clockIn, ClockOutAt = clockIn.AddMinutes(worked), DurationMinutes = worked,
                            CreatedAt = clockIn, CreatedById = userId
                        });
                    }
                    minutesByTask[task.Id] = minutesByTask.GetValueOrDefault(task.Id) + worked;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // Clocked time shows on the task: hours only ever go up, so a re-run never undoes work done in the app.
        foreach (var task in tasksByEmployee.Values.SelectMany(t => t).DistinctBy(t => t.Id))
        {
            if (!minutesByTask.TryGetValue(task.Id, out var minutes))
                continue;
            var hours = Math.Round(minutes / 60m * 2, MidpointRounding.AwayFromZero) / 2;
            if (hours <= task.CompletedHours)
                continue;
            task.CompletedHours = hours;
            if (task.CompletedAt is null && task.EstimatedHours is > 0)
                task.ProgressPercent = Math.Max(task.ProgressPercent, (int)Math.Min(90, hours * 100 / task.EstimatedHours.Value));
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A task is planned from the day it was started (or the window start) until the day it was completed.</summary>
    private static bool PlannableOn(WorkTask task, DateOnly date)
    {
        var started = task.StartedAt is { } s ? DateOnly.FromDateTime(s.ToOffset(ColomboOffset).DateTime) : DateOnly.MinValue;
        var completed = task.CompletedAt is { } c ? DateOnly.FromDateTime(c.ToOffset(ColomboOffset).DateTime) : DateOnly.MaxValue;
        return date >= started && date <= completed;
    }

    /// <summary>
    /// Demo tasks are created all at once with no due dates, so the first run spreads them over the window: created
    /// before it, started inside it, finished tasks completed on a past day, open ones due from last week to a few weeks
    /// out (some overdue). Only tasks that still have no due date are touched - a date set in the app is never moved.
    /// </summary>
    private static void GiveTasksALifecycle(SeedContext ctx, IEnumerable<WorkTask> tasks, HashSet<Guid> doneStatusIds)
    {
        var span = ctx.Window.Today.DayNumber - ctx.Window.From.DayNumber;
        foreach (var task in tasks)
        {
            if (task.DueDate is not null)
                continue;
            var key = task.Id.ToString("N");
            var isDone = doneStatusIds.Contains(task.StatusId) || task.CompletedAt is not null;
            var startOffset = Pick($"start:{key}", Math.Max(1, span - 10));
            DateOnly startDate;

            // Tasks opened inside the window keep their day; the original demo tasks predate it.
            var createdDay = DateOnly.FromDateTime(task.CreatedAt.ToOffset(ColomboOffset).DateTime);
            if (createdDay > ctx.Window.From && createdDay < ctx.Window.Today)
                startOffset = Math.Max(createdDay.DayNumber - ctx.Window.From.DayNumber, Math.Min(span - 1, startOffset));
            else
                task.CreatedAt = At(ctx.Window.From.AddDays(-Pick($"created:{key}", 20)), 10, 0);
            startDate = ctx.Window.From.AddDays(startOffset);
            if (isDone)
            {
                var doneDate = startDate.AddDays(3 + Pick($"done:{key}", 10));
                if (doneDate >= ctx.Window.Today)
                    doneDate = ctx.Window.Today.AddDays(-1);
                task.StartedAt = At(startDate, 9, 40);
                task.CompletedAt = At(doneDate, 16, 30);
                task.DueDate = doneDate.AddDays(Pick($"due:{key}", 5) - 1);
            }
            else
            {
                // Still at 0% means nobody has started it yet.
                task.StartedAt = task.ProgressPercent == 0 ? null : At(startDate, 9, 40);
                task.DueDate = ctx.Window.Today.AddDays(Pick($"due:{key}", 28) - 6);
            }
            task.UpdatedAt = task.CompletedAt ?? task.StartedAt ?? task.CreatedAt;
        }
    }

    /// <summary>The owner gets a handful of open tasks of their own (earlier seeds gave them "due today" filler tasks).</summary>
    private static async Task EnsureOwnerAssignmentsAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Db;
        var ownerId = ctx.Owner.Id;
        var have = await db.TaskAssignments.CountAsync(a => a.EmployeeId == ownerId, ct);
        if (have >= OwnerTaskCount)
            return;

        var projectId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:project:{OwnerProjectKey}");
        var doneStatusIds = db.TaskStatuses.Where(s => s.TenantId == DapiTenantId && s.MarksTaskComplete).Select(s => s.Id);
        var candidates = await db.WorkTasks
            .Where(t => t.TenantId == DapiTenantId && t.ProjectId == projectId && !t.IsDeleted
                && t.ParentTaskId == null && !doneStatusIds.Contains(t.StatusId)
                && !db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == ownerId))
            .OrderBy(t => t.ShortId)
            .Take(OwnerTaskCount - have)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var taskId in candidates)
        {
            var id = SeedId($"owner-assignment:{taskId:N}");
            if (await db.TaskAssignments.AnyAsync(a => a.Id == id, ct))
                continue;
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = id,
                TaskId = taskId,
                UserId = DapiOwnerUserId,
                EmployeeId = ownerId,
                AssignedById = DapiOwnerUserId,
                AssignedAt = At(ctx.Window.From, 10, 0)
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
