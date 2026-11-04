using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiHistorySeeder
{
    private const int SprintDays = 14;
    private const int ExtraTasksPerModule = 6;
    private const string ToDo = "To Do";
    private const string InProcess = "In Process";
    private const string Review = "Review";
    private const string Done = "Done";

    private static readonly (string Category, string Priority, string Title)[] TaskIdeas =
    [
        ("task", "medium", "Write acceptance criteria"),
        ("story", "high", "Build the main flow"),
        ("bug", "high", "Fix validation error on save"),
        ("task", "low", "Update the user guide"),
        ("feature", "medium", "Add export to CSV"),
        ("bug", "medium", "Fix timezone shift in reports"),
        ("story", "medium", "Settings screen for admins"),
        ("task", "high", "Performance pass on list view"),
        ("bug", "low", "Fix truncated labels on mobile"),
        ("task", "medium", "Code review follow-ups"),
        ("feature", "high", "Role-based access for the module"),
        ("task", "low", "Clean up feature flags"),
    ];

    private static readonly string[] ProgressComments =
    [
        "Started on this today, first pass should be up by tomorrow.",
        "Pushed a draft - can someone take a look at the approach?",
        "Blocked on the API contract, waiting for the backend changes.",
        "Unblocked now, carrying on.",
        "Half way there. The edge cases are taking longer than expected.",
        "Moved to review. Test notes are in the description.",
        "Found one more case while testing, fixing it before closing.",
        "Demoed this in the stand-up, everyone's happy with it.",
    ];

    private static readonly string[] ReplyComments =
    [
        "Looks good to me.",
        "Thanks! I'll review it this afternoon.",
        "Can we keep the old behaviour behind a flag for now?",
        "Nice work 👏",
        "Let's pair on it after lunch.",
    ];

    private static readonly string[] Reactions = ["👍", "🎉", "👀", "✅", "🙌"];

    /// <summary>
    /// Two months of real-looking project work on the five demo projects: two-week sprints (finished ones, the active one
    /// that contains today, the next one still in draft), new tasks opened week by week in every module, every task given
    /// a lifecycle (created, started, reviewed, done or still open, some overdue) with its status, progress and edit history,
    /// and team comments with replies and reactions. Deterministic ids: a re-run only adds what is new.
    /// </summary>
    private static async Task SeedWorkHistoryAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Db;
        var people = ctx.Employees.ToDictionary(e => e.Id);
        foreach (var tree in WorkManagementDapiDemoData.ProjectTrees)
        {
            var projectId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:project:{tree.ProjectKey}");
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null)
                continue;

            var statuses = await db.TaskStatuses
                .Where(s => s.TenantId == DapiTenantId && s.ProjectId == projectId && s.ObjectiveId == null)
                .ToDictionaryAsync(s => s.Name, s => s.Id, ct);
            if (!new[] { ToDo, InProcess, Review, Done }.All(statuses.ContainsKey))
                continue;
            var categories = await db.TaskCategories
                .Where(c => c.TenantId == DapiTenantId && c.ProjectId == projectId)
                .ToDictionaryAsync(c => c.Name.ToLowerInvariant(), c => c.Id, ct);
            if (categories.Count == 0)
                continue;

            var sprints = await EnsureSprintsAsync(ctx, project, ct);
            await AddModuleTasksAsync(ctx, project, statuses, categories, people, ct);

            var tasks = await db.WorkTasks
                .Where(t => t.TenantId == DapiTenantId && t.ProjectId == projectId && !t.IsDeleted && t.ParentTaskId == null)
                .ToListAsync(ct);
            var doneIds = new HashSet<Guid> { statuses[Done] };
            GiveTasksALifecycle(ctx, tasks, doneIds);
            PlaceInSprints(ctx, tasks, sprints, doneIds);
            await db.SaveChangesAsync(ct);

            var assignees = await AssigneesAsync(db, tasks.Select(t => t.Id).ToList(), ct);
            await SeedTaskHistoryAsync(ctx, tasks, statuses, assignees, people, ct);
            await SeedCommentsAsync(ctx, tasks, assignees, people, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed record SprintSlot(Sprint Sprint, DateOnly Start, DateOnly End);

    /// <summary>Back-to-back two-week sprints from the first Monday of the window; the one holding today is active,
    /// earlier ones are complete, and the next one waits as a draft.</summary>
    private static async Task<List<SprintSlot>> EnsureSprintsAsync(SeedContext ctx, Project project, CancellationToken ct)
    {
        var db = ctx.Db;
        var start = ctx.Window.From;
        while (start.DayOfWeek != DayOfWeek.Monday)
            start = start.AddDays(1);
        // Numbered from a fixed epoch so a sprint keeps its name (and id) as the window rolls forward.
        var epoch = new DateOnly(2026, 1, 5);
        var slots = new List<SprintSlot>();
        for (var sprintStart = start; sprintStart <= ctx.Window.Today.AddDays(SprintDays); sprintStart = sprintStart.AddDays(SprintDays))
        {
            var number = (sprintStart.DayNumber - epoch.DayNumber) / SprintDays + 1;
            var aligned = epoch.AddDays((number - 1) * SprintDays);
            var end = aligned.AddDays(SprintDays - 3); // Monday .. Friday of week two
            var status = end < ctx.Window.Today ? SprintStatuses.Complete
                : aligned <= ctx.Window.Today ? SprintStatuses.Active
                : SprintStatuses.Draft;
            var id = SeedId($"sprint:{project.Id:N}:{number}");
            var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (sprint is null)
            {
                sprint = new Sprint
                {
                    Id = id,
                    TenantId = DapiTenantId,
                    ProjectId = project.Id,
                    Name = $"{project.Identifier} Sprint {number}",
                    Goal = SprintGoal(number),
                    CreatedAt = At(aligned.AddDays(-4), 11, 0),
                    CreatedById = DapiOwnerUserId,
                };
                db.Sprints.Add(sprint);
                SprintLog(db, sprint, ctx.Owner.Id, SprintActivityActions.Created, null, SprintStatuses.Draft, sprint.CreatedAt);
            }

            // Sprints only move forward: draft -> active -> complete, as the window rolls.
            if (sprint.Status == SprintStatuses.Draft && status != SprintStatuses.Draft)
            {
                sprint.Status = SprintStatuses.Active;
                sprint.StartDate = aligned;
                sprint.EndDate = end;
                SprintLog(db, sprint, ctx.Owner.Id, SprintActivityActions.Started, SprintStatuses.Draft, SprintStatuses.Active, At(aligned, 9, 45));
            }
            if (sprint.Status == SprintStatuses.Active && status == SprintStatuses.Complete)
            {
                sprint.Status = SprintStatuses.Complete;
                sprint.CompletedAt = At(end, 17, 30);
                SprintLog(db, sprint, ctx.Owner.Id, SprintActivityActions.Completed, SprintStatuses.Active, SprintStatuses.Complete, sprint.CompletedAt.Value);
            }
            slots.Add(new SprintSlot(sprint, aligned, end));
        }
        await db.SaveChangesAsync(ct);
        return slots;
    }

    private static string SprintGoal(int number) => (number % 4) switch
    {
        0 => "Close the open bugs from the last release and stabilise the build.",
        1 => "Ship the next module slice end to end, behind a flag.",
        2 => "Performance and polish: list views, exports, empty states.",
        _ => "Customer feedback round: fix the top five requests.",
    };

    private static void SprintLog(
        ApplicationDbContext db, Sprint sprint, Guid employeeId, string action, string? from, string? to, DateTimeOffset at)
    {
        db.SprintActivityLogs.Add(new SprintActivityLog
        {
            Id = SeedId($"sprint-log:{sprint.Id:N}:{action}"),
            TenantId = DapiTenantId,
            SprintId = sprint.Id,
            EmployeeId = employeeId,
            Action = action,
            FromStatus = from,
            ToStatus = to,
            OccurredAt = at,
            CreatedAt = at,
            CreatedById = DapiOwnerUserId,
        });
    }

    /// <summary>New tasks in every leaf module, opened across the window by the module's people.</summary>
    private static async Task AddModuleTasksAsync(
        SeedContext ctx, Project project, Dictionary<string, Guid> statuses, Dictionary<string, Guid> categories,
        IReadOnlyDictionary<Guid, Domain.Features.CoreHr.Entities.Employee> people, CancellationToken ct)
    {
        var db = ctx.Db;
        var objectives = await db.Objectives
            .Where(o => o.TenantId == DapiTenantId && o.ProjectId == project.Id && !o.IsDeleted)
            .Select(o => new { o.Id, o.ParentObjectiveId, o.Title, o.OwnerId })
            .ToListAsync(ct);
        var parents = objectives.Where(o => o.ParentObjectiveId != null).Select(o => o.ParentObjectiveId!.Value).ToHashSet();
        var leaves = objectives.Where(o => !parents.Contains(o.Id)).OrderBy(o => o.Title).ToList();
        var members = (await db.ProjectMembers
                .Where(m => m.TenantId == DapiTenantId && m.ProjectId == project.Id && !m.IsDeleted)
                .Select(m => new { m.ObjectiveId, m.EmployeeId })
                .ToListAsync(ct))
            .GroupBy(m => m.ObjectiveId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.EmployeeId).Where(people.ContainsKey).Distinct().ToList());
        var span = ctx.Window.Today.DayNumber - ctx.Window.From.DayNumber;

        for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
        {
            var leaf = leaves[leafIndex];
            var team = members.GetValueOrDefault(leaf.Id) ?? [];
            if (people.ContainsKey(leaf.OwnerId) && !team.Contains(leaf.OwnerId))
                team.Insert(0, leaf.OwnerId);
            if (team.Count == 0)
                continue;

            for (var j = 0; j < ExtraTasksPerModule; j++)
            {
                var taskId = SeedId($"work-task:{leaf.Id:N}:{j}");
                if (db.WorkTasks.Local.Any(t => t.Id == taskId) || await db.WorkTasks.AnyAsync(t => t.Id == taskId, ct))
                    continue;

                // Opened week by week across the window; the creation day decides how far the task got.
                var created = ctx.Window.From.AddDays(Math.Min(span - 1, j * span / ExtraTasksPerModule + Pick($"c:{taskId:N}", 6)));
                var age = ctx.Window.Today.DayNumber - created.DayNumber;
                var roll = Pick($"s:{taskId:N}", 10);
                var statusName = age > 30 ? (roll < 8 ? Done : Review)
                    : age > 14 ? (roll < 5 ? Done : roll < 7 ? Review : InProcess)
                    : age > 5 ? (roll < 2 ? Done : roll < 4 ? Review : roll < 8 ? InProcess : ToDo)
                    : (roll < 5 ? InProcess : ToDo);
                var idea = TaskIdeas[(leafIndex * 3 + j) % TaskIdeas.Length];
                var categoryId = categories.GetValueOrDefault(idea.Category) == Guid.Empty
                    ? categories.Values.First()
                    : categories[idea.Category];
                var assignee = team[(j + leafIndex) % team.Count];
                var creator = people[team[0]];
                var estimate = 4m + Pick($"e:{taskId:N}", 13);
                var done = statusName == Done;

                db.WorkTasks.Add(new WorkTask
                {
                    Id = taskId,
                    TenantId = DapiTenantId,
                    ProjectId = project.Id,
                    ObjectiveId = leaf.Id,
                    ShortId = $"{project.Identifier}-{project.NextTaskNumber++}",
                    Title = idea.Title,
                    Description = WebUtility.HtmlEncode($"{idea.Title} for {leaf.Title}."),
                    CategoryId = categoryId,
                    StatusId = statuses[statusName],
                    Priority = idea.Priority,
                    StoryPoints = 1 + Pick($"sp:{taskId:N}", 8),
                    EstimatedHours = estimate,
                    CompletedHours = done ? estimate : 0m,
                    ProgressPercent = done ? 100 : statusName == Review ? 90 : statusName == InProcess ? 20 + Pick($"p:{taskId:N}", 50) : 0,
                    CreatedAt = At(created, 10, 0),
                    CreatedById = creator.UserId,
                });
                db.TaskAssignments.Add(new TaskAssignment
                {
                    Id = SeedId($"work-task-assignment:{taskId:N}"),
                    TaskId = taskId,
                    UserId = people[assignee].UserId,
                    EmployeeId = assignee,
                    AssignedById = creator.UserId,
                    AssignedAt = At(created, 10, 5),
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Finished tasks sit in the sprint they were finished in; open ones were carried into the active sprint,
    /// apart from a few fresh to-dos left in the backlog or planned for the next sprint.</summary>
    private static void PlaceInSprints(SeedContext ctx, List<WorkTask> tasks, List<SprintSlot> sprints, HashSet<Guid> doneIds)
    {
        if (sprints.Count == 0)
            return;
        var active = sprints.FirstOrDefault(s => s.Sprint.Status == SprintStatuses.Active);
        var draft = sprints.FirstOrDefault(s => s.Sprint.Status == SprintStatuses.Draft);
        foreach (var task in tasks.Where(t => t.SprintId is null))
        {
            var key = task.Id.ToString("N");
            if (doneIds.Contains(task.StatusId) && task.CompletedAt is { } completed)
            {
                var day = DateOnly.FromDateTime(completed.ToOffset(ColomboOffset).DateTime);
                task.SprintId = sprints.FirstOrDefault(s => day >= s.Start && day <= s.End.AddDays(2))?.Sprint.Id;
                continue;
            }
            var roll = Pick($"sprint:{key}", 10);
            task.SprintId = task.StartedAt is null && roll >= 7 ? (roll == 9 ? draft?.Sprint.Id : null) : active?.Sprint.Id;
        }
    }

    private static async Task<Dictionary<Guid, Guid>> AssigneesAsync(ApplicationDbContext db, List<Guid> taskIds, CancellationToken ct) =>
        (await db.TaskAssignments
            .Where(a => taskIds.Contains(a.TaskId))
            .Select(a => new { a.TaskId, a.EmployeeId, a.AssignedAt })
            .ToListAsync(ct))
        .GroupBy(a => a.TaskId)
        .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AssignedAt).First().EmployeeId);

    /// <summary>Status moves (To Do -> In Process -> Review -> Done) on the task's own dates, the matching progress
    /// steps, and the odd due-date or priority edit - what the task's History tab shows.</summary>
    private static async Task SeedTaskHistoryAsync(
        SeedContext ctx, List<WorkTask> tasks, Dictionary<string, Guid> statuses, Dictionary<Guid, Guid> assignees,
        IReadOnlyDictionary<Guid, Domain.Features.CoreHr.Entities.Employee> people, CancellationToken ct)
    {
        var db = ctx.Db;
        var taskIds = tasks.Select(t => t.Id).ToList();
        var logged = (await db.TaskStatusChangeLogs.Where(l => taskIds.Contains(l.TaskId)).Select(l => l.TaskId).ToListAsync(ct)).ToHashSet();
        var nameOf = statuses.ToDictionary(kv => kv.Value, kv => kv.Key);

        foreach (var task in tasks)
        {
            if (logged.Contains(task.Id) || task.StartedAt is not { } started || !assignees.TryGetValue(task.Id, out var who)
                || !people.TryGetValue(who, out var person) || !nameOf.TryGetValue(task.StatusId, out var current))
                continue;
            var key = task.Id.ToString("N");
            var reviewAt = task.CompletedAt?.AddHours(-20) ?? started.AddDays(2 + Pick($"rv:{key}", 4)).AddHours(5);
            if (reviewAt > ctx.Now)
                reviewAt = ctx.Now.AddHours(-3);

            var steps = new List<(string From, string To, DateTimeOffset At, int Percent)> { (ToDo, InProcess, started, 10) };
            if (current is Review or Done)
                steps.Add((InProcess, Review, reviewAt, 90));
            if (current == Done && task.CompletedAt is { } completed)
                steps.Add((Review, Done, completed, 100));

            var previous = 0;
            for (var i = 0; i < steps.Count; i++)
            {
                var (from, to, at, percent) = steps[i];
                db.TaskStatusChangeLogs.Add(new TaskStatusChangeLog
                {
                    Id = SeedId($"task-status-log:{key}:{i}"),
                    TenantId = DapiTenantId,
                    TaskId = task.Id,
                    EmployeeId = who,
                    FromStatusId = statuses[from],
                    ToStatusId = statuses[to],
                    ChangedAt = at,
                    CreatedAt = at,
                    CreatedById = person.UserId,
                });
                // A mid-way push between starting and review, like the tray's progress push.
                if (i == 1 || (i == 0 && steps.Count == 1 && task.ProgressPercent > 10))
                {
                    var midPercent = steps.Count == 1 ? task.ProgressPercent : 50;
                    var midAt = steps.Count == 1 ? started.AddDays(1).AddHours(6) : started + (at - started) / 2;
                    if (midAt < ctx.Now)
                    {
                        AddPercentLog(db, key, "push", task.Id, who, person.UserId, previous, midPercent, TaskPercentageLogSources.Push, midAt);
                        previous = midPercent;
                    }
                }
                if (percent > previous && (i > 0 || steps.Count > 1))
                {
                    AddPercentLog(db, key, $"status-{i}", task.Id, who, person.UserId, previous, percent, TaskPercentageLogSources.StatusChange, at);
                    previous = percent;
                }
            }

            // The odd edit: a pushed-out due date, a priority bump.
            var edit = Pick($"edit:{key}", 5);
            if (edit == 0 && task.DueDate is { } due)
                AddEditLog(db, key, task.Id, who, person.UserId, "dueDate", due.AddDays(-4).ToString("yyyy-MM-dd"), due.ToString("yyyy-MM-dd"),
                    "Scope grew after review, moving the date out.", started.AddDays(1));
            else if (edit == 1)
                AddEditLog(db, key, task.Id, who, person.UserId, "priority", "medium", task.Priority, null, started.AddHours(3));
        }
    }

    private static void AddPercentLog(
        ApplicationDbContext db, string key, string step, Guid taskId, Guid employeeId, Guid userId, int from, int to, string source,
        DateTimeOffset at)
    {
        db.TaskPercentageLogs.Add(new TaskPercentageLog
        {
            Id = SeedId($"task-percent-log:{key}:{step}"),
            TenantId = DapiTenantId,
            TaskId = taskId,
            EmployeeId = employeeId,
            PreviousPercent = from,
            NewPercent = to,
            Source = source,
            ChangedAt = at,
            CreatedAt = at,
            CreatedById = userId,
        });
    }

    private static void AddEditLog(
        ApplicationDbContext db, string key, Guid taskId, Guid employeeId, Guid userId, string field, object? oldValue, object? newValue,
        string? reason, DateTimeOffset at)
    {
        db.TaskEditLogs.Add(new TaskEditLog
        {
            Id = SeedId($"task-edit-log:{key}:{field}"),
            TenantId = DapiTenantId,
            TaskId = taskId,
            EmployeeId = employeeId,
            Source = TaskEditLogSources.Direct,
            OldValuesJson = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = oldValue }),
            NewValuesJson = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = newValue }),
            Reason = reason,
            CreatedAt = at,
            CreatedById = userId,
        });
    }

    /// <summary>Most started tasks get a short thread: the assignee's progress notes, a teammate's reply, and a reaction
    /// or two (one per person - the app's single-reaction rule).</summary>
    private static async Task SeedCommentsAsync(
        SeedContext ctx, List<WorkTask> tasks, Dictionary<Guid, Guid> assignees,
        IReadOnlyDictionary<Guid, Domain.Features.CoreHr.Entities.Employee> people, CancellationToken ct)
    {
        var db = ctx.Db;
        var taskIds = tasks.Select(t => t.Id).ToList();
        var commented = (await db.TaskComments.Where(c => taskIds.Contains(c.TaskId)).Select(c => c.TaskId).ToListAsync(ct)).ToHashSet();
        var teamOf = assignees.Values.Distinct().ToList();

        foreach (var task in tasks)
        {
            var key = task.Id.ToString("N");
            if (commented.Contains(task.Id) || task.StartedAt is not { } started || !assignees.TryGetValue(task.Id, out var author)
                || !people.TryGetValue(author, out var person) || Pick($"cm:{key}", 10) < 3)
                continue;
            var end = task.CompletedAt ?? ctx.Now;
            var count = 1 + Pick($"cn:{key}", 3);
            for (var i = 0; i < count; i++)
            {
                var at = started + (end - started) * (i + 1) / (count + 1);
                if (at > ctx.Now)
                    break;
                var commentId = SeedId($"task-comment:{key}:{i}");
                db.TaskComments.Add(new TaskComment
                {
                    Id = commentId,
                    TenantId = DapiTenantId,
                    TaskId = task.Id,
                    EmployeeId = author,
                    Content = $"<p>{WebUtility.HtmlEncode(ProgressComments[(Pick($"ct:{key}", 8) + i) % ProgressComments.Length])}</p>",
                    CreatedAt = at,
                    CreatedById = person.UserId,
                });

                var mateId = teamOf.Count > 1 ? teamOf[(Pick($"mate:{key}:{i}", teamOf.Count))] : ctx.Owner.Id;
                if (mateId == author)
                    mateId = ctx.ManagerOrOwner(author);
                if (!people.TryGetValue(mateId, out var mate))
                    continue;
                if (Pick($"rp:{key}:{i}", 2) == 0 && at.AddHours(2) < ctx.Now)
                {
                    db.TaskComments.Add(new TaskComment
                    {
                        Id = SeedId($"task-comment:{key}:{i}:reply"),
                        TenantId = DapiTenantId,
                        TaskId = task.Id,
                        EmployeeId = mateId,
                        ParentCommentId = commentId,
                        Content = $"<p>{WebUtility.HtmlEncode(ReplyComments[Pick($"rt:{key}:{i}", ReplyComments.Length)])}</p>",
                        CreatedAt = at.AddHours(2),
                        CreatedById = mate.UserId,
                    });
                }
                if (Pick($"rx:{key}:{i}", 3) != 0)
                {
                    db.TaskCommentReactions.Add(new TaskCommentReaction
                    {
                        Id = SeedId($"task-comment-reaction:{key}:{i}:{mateId:N}"),
                        TenantId = DapiTenantId,
                        CommentId = commentId,
                        EmployeeId = mateId,
                        Emoji = Reactions[Pick($"re:{key}:{i}", Reactions.Length)],
                        CreatedAt = at.AddMinutes(30),
                        CreatedById = mate.UserId,
                    });
                }
            }
        }
    }
}
