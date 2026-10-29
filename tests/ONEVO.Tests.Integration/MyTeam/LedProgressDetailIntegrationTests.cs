using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedEmployeeHours;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedPeriodProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedProgressTasks;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Xunit;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Team Progress detail endpoints (led-progress/employees, led-progress/tasks,
/// led-progress/period, led-progress/weekly-completion) against real PostgreSQL. Requires Docker (Testcontainers PostgreSQL).</summary>
public sealed class LedProgressDetailIntegrationTests
{
    // Wednesday: ISO week = Mon 2026-10-19 .. Sun 2026-10-25; month = 2026-10-01 .. 2026-10-31.
    private static readonly DateOnly FixedToday = new(2026, 10, 21);

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(FixedToday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        public DateOnly Today => FixedToday;
    }

    private sealed class StubUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private static WorkLeadershipService WorkLeadership(ApplicationDbContext db) => new(
        new EfObjectiveRepository(db),
        new EfProjectMemberRepository(db),
        new EfWorkApprovalRequestRepository(db),
        new WorkApprovalEligibility(
            new EfWorkApprovalRequestRepository(db),
            new WorkHierarchyService(
                new EfObjectiveRepository(db),
                new MilestoneMembershipCoordinator(
                    new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
                    new EfProjectMemberInvitationRepository(db),
                    new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new ONEVO.Infrastructure.Persistence.Repositories.EfEntityAssetRepository(db)),
                    new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()))),
            new EfObjectiveRepository(db)),
        NullLogger<WorkLeadershipService>.Instance);

    private static GetLedEmployeeHoursQueryHandler EmployeeHandler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId), new FixedClock(), new EfEmployeeRepository(db), WorkLeadership(db), new EfLedProgressReadRepository(db));

    private static GetLedProgressTasksQueryHandler TaskHandler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId), new FixedClock(), new EfEmployeeRepository(db), WorkLeadership(db), new EfLedProgressReadRepository(db));

    private static GetLedPeriodProgressQueryHandler PeriodHandler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId), new FixedClock(), new EfEmployeeRepository(db), WorkLeadership(db), new EfLedProgressReadRepository(db));

    private static GetLedWeeklyCompletionQueryHandler WeeklyHandler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId), new FixedClock(), new EfEmployeeRepository(db), WorkLeadership(db),
        new EfProjectRepository(db), new EfLedProgressReadRepository(db));

    private sealed record Seeded(
        MyTeamDb Db, Guid LeadUserId, Guid OutsiderUserId, Guid ProjectId, Guid OtherProjectId, Guid ModuleId, Guid SubId,
        Guid AliceId, Guid BobId, Guid OutsiderId, IReadOnlyDictionary<string, Guid> Tasks);

    /// <summary>
    /// Lead owns Module (with child Sub). Top-level tasks in scope:
    ///   Module: t1 Done-status 0% 5h Alice (completed), t2 Doing 100% 2h Alice (completed),
    ///           t3 Doing 30% 3h Bob (inProgress), t4 Doing 0% no estimate Bob (notStarted, 0h),
    ///           t5 Doing 0% 7h unassigned (notStarted)
    ///   Sub:    t8 Doing 50% 4h Alice (inProgress), t9 Done 1h unassigned (completed)
    /// Never counted: a 50h subtask of t3 (Alice), a soft-deleted 40h task (Alice), and a 99h task
    /// in a project the lead does not lead (Alice).
    /// </summary>
    private static async Task<Seeded> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = FixedToday;
        var tasks = new Dictionary<string, Guid>();
        Guid deletedTaskId;
        Seeded seeded;
        await using (var db = helper.NewContext())
        {
            var creator = helper.AddEmployee(db);
            var lead = helper.AddEmployee(db, lastName: "Lead");
            var alice = helper.AddEmployee(db, lastName: "Alpha");
            var bob = helper.AddEmployee(db, lastName: "Bravo");
            var outsider = helper.AddEmployee(db, lastName: "Outsider");
            var (projectId, rootId) = helper.AddProject(db, creator.Id);
            var module = helper.AddModule(db, projectId, rootId, lead.Id, allocatedHours: 100);
            var sub = helper.AddModule(db, projectId, module, alice.Id, allocatedHours: 40);
            helper.AddMember(db, projectId, module, lead.Id);
            helper.AddMember(db, projectId, sub, alice.Id);
            var doing = helper.AddStatus(db, projectId, false);
            var done = helper.AddStatus(db, projectId, true);

            WorkTask Add(string key, Guid objective, Guid status, int progress, decimal? hours, Guid? assignee, DateOnly? due = null,
                Guid? parent = null, Guid? project = null)
            {
                var task = helper.AddTask(db, project ?? projectId, objective, status, progress, due, parent, assignee, hours);
                task.ShortId = $"TP-{tasks.Count + 1}";
                task.Title = $"{key} title";
                tasks[key] = task.Id;
                return task;
            }

            Add("t1", module, done, 0, 5, alice.Id, today.AddDays(3));
            Add("t2", module, doing, 100, 2, alice.Id, today.AddDays(1));
            var t3 = Add("t3", module, doing, 30, 3, bob.Id, today.AddDays(2));
            Add("t4", module, doing, 0, null, bob.Id);
            Add("t5", module, doing, 0, 7, null, today.AddDays(10));
            Add("t8", sub, doing, 50, 4, alice.Id, today.AddDays(5));
            Add("t9", sub, done, 0, 1, null);
            Add("subtask", module, doing, 0, 50, alice.Id, parent: t3.Id);
            deletedTaskId = Add("deleted", module, doing, 0, 40, alice.Id).Id;

            var (otherProjectId, otherRoot) = helper.AddProject(db, outsider.Id);
            var otherStatus = helper.AddStatus(db, otherProjectId, false);
            Add("foreign", otherRoot, otherStatus, 0, 99, alice.Id, project: otherProjectId);

            await db.SaveChangesAsync();
            seeded = new Seeded(helper, lead.UserId, outsider.UserId, projectId, otherProjectId, module, sub,
                alice.Id, bob.Id, outsider.Id, tasks);
        }

        await using (var db = helper.NewContext())
        {
            db.WorkTasks.Remove((await db.WorkTasks.FindAsync(deletedTaskId))!);
            await db.SaveChangesAsync();
        }

        return seeded;
    }

    private static async Task<LedEmployeeHoursPage> Employees(Seeded s, GetLedEmployeeHoursQuery query, Guid? userId = null)
    {
        await using var db = s.Db.NewContext();
        var result = await EmployeeHandler(db, s.Db.TenantId, userId ?? s.LeadUserId).Handle(query, default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private static async Task<PagedResult<LedProgressTaskItem>>
        Tasks(Seeded s, GetLedProgressTasksQuery query, Guid? userId = null)
    {
        await using var db = s.Db.NewContext();
        var result = await TaskHandler(db, s.Db.TenantId, userId ?? s.LeadUserId).Handle(query, default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    [Fact]
    public async Task Employee_hours_classify_like_the_summary_trigger_and_report_unassigned_separately()
    {
        var s = await SeedAsync();

        var page = (await Employees(s, new GetLedEmployeeHoursQuery()));

        page.TotalCount.Should().Be(2);
        page.Items.Select(i => i.EmployeeId).Should().Equal(s.AliceId, s.BobId);
        var alice = page.Items[0];
        (alice.CompletedHours, alice.InProgressHours, alice.NotStartedHours, alice.TotalHours).Should().Be((7m, 4m, 0m, 11m));
        alice.TaskCount.Should().Be(3, "subtasks, soft-deleted tasks and out-of-scope tasks never count");
        (alice.SharedTaskCount, alice.SharedHours).Should().Be((0, 0m));
        alice.DisplayName.Should().Be("Team Alpha");
        var bob = page.Items[1];
        (bob.CompletedHours, bob.InProgressHours, bob.NotStartedHours, bob.TotalHours, bob.TaskCount).Should().Be((0m, 3m, 0m, 3m, 2));

        (page.Unassigned.CompletedHours, page.Unassigned.InProgressHours, page.Unassigned.NotStartedHours,
            page.Unassigned.TotalHours, page.Unassigned.TaskCount).Should().Be((1m, 0m, 7m, 8m, 2));
    }

    [Fact]
    public async Task Employee_hours_match_the_led_progress_summary_totals()
    {
        var s = await SeedAsync();
        await using var db = s.Db.NewContext();
        // Recompute with the trigger's own function so the comparison does not depend on the order
        // EF happened to batch the seed INSERTs in (a task row inserted before its objective or
        // status row is summarised against a missing row).
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT wm_refresh_module_hour_summary({s.ModuleId}), wm_refresh_module_hour_summary({s.SubId})");
        var summaries = await new EfModuleHourSummaryRepository(db)
            .ListByObjectiveIdsAsync(s.Db.TenantId, new[] { s.ModuleId, s.SubId });
        var page = (await Employees(s, new GetLedEmployeeHoursQuery()));

        // Single-assignee data: assigned + unassigned == the trigger-maintained totals.
        (page.Items.Sum(i => i.CompletedHours) + page.Unassigned.CompletedHours)
            .Should().Be(summaries.Values.Sum(v => v.CompletedHours));
        (page.Items.Sum(i => i.InProgressHours) + page.Unassigned.InProgressHours)
            .Should().Be(summaries.Values.Sum(v => v.InProgressHours));
        (page.Items.Sum(i => i.NotStartedHours) + page.Unassigned.NotStartedHours)
            .Should().Be(summaries.Values.Sum(v => v.NotStartedHours));
    }

    [Fact]
    public async Task Multi_assignee_task_counts_full_hours_for_each_assignee_and_is_flagged_shared()
    {
        var s = await SeedAsync();
        await using (var db = s.Db.NewContext())
        {
            // The schema currently allows one assignee per task (EnforceSingleTaskAssignee). The
            // contract still requires shared-task handling, so lift the index in this test's own
            // cloned database only, to exercise the aggregation SQL with two assignees.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS ix_task_assignments_one_per_task");
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = Guid.NewGuid(), TaskId = s.Tasks["t3"], EmployeeId = s.AliceId, UserId = Guid.NewGuid(),
                AssignedById = s.AliceId, AssignedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var page = (await Employees(s, new GetLedEmployeeHoursQuery()));

        var alice = page.Items.Single(i => i.EmployeeId == s.AliceId);
        var bob = page.Items.Single(i => i.EmployeeId == s.BobId);
        (alice.InProgressHours, alice.TotalHours, alice.TaskCount, alice.SharedTaskCount, alice.SharedHours)
            .Should().Be((7m, 14m, 4, 1, 3m));
        (bob.InProgressHours, bob.TotalHours, bob.SharedTaskCount, bob.SharedHours).Should().Be((3m, 3m, 1, 3m));

        var tasks = await Tasks(s, new GetLedProgressTasksQuery(Search: "TP-3"));
        var shared = tasks.Items.Should().ContainSingle().Subject;
        shared.IsShared.Should().BeTrue();
        shared.Assignees.Select(a => a.EmployeeId).Should().BeEquivalentTo(new[] { s.AliceId, s.BobId });
        (await Tasks(s, new GetLedProgressTasksQuery(Search: "TP-1"))).Items.Single().IsShared.Should().BeFalse();
    }

    [Fact]
    public async Task Employee_search_sort_paging_and_filters_run_in_the_database()
    {
        var s = await SeedAsync();

        (await Employees(s, new GetLedEmployeeHoursQuery(Search: "BRAVO"))).Items.Select(i => i.EmployeeId)
            .Should().Equal(s.BobId);
        (await Employees(s, new GetLedEmployeeHoursQuery(Search: "team alp"))).Items.Select(i => i.EmployeeId)
            .Should().Equal(s.AliceId);
        (await Employees(s, new GetLedEmployeeHoursQuery(Sort: "inProgressHours", Direction: "asc"))).Items
            .Select(i => i.EmployeeId).Should().Equal(s.BobId, s.AliceId);
        (await Employees(s, new GetLedEmployeeHoursQuery(Sort: "name", Direction: "desc"))).Items
            .Select(i => i.EmployeeId).Should().Equal(s.BobId, s.AliceId);

        var second = (await Employees(s, new GetLedEmployeeHoursQuery(PageNumber: 2, PageSize: 1)));
        second.Items.Select(i => i.EmployeeId).Should().Equal(s.BobId);
        (second.TotalCount, second.TotalPages, second.HasNext, second.HasPrevious).Should().Be((2, 2, false, true));

        var sub = (await Employees(s, new GetLedEmployeeHoursQuery(ObjectiveId: s.SubId)));
        sub.Items.Should().ContainSingle(i => i.EmployeeId == s.AliceId && i.TotalHours == 4 && i.InProgressHours == 4);
        (sub.Unassigned.CompletedHours, sub.Unassigned.TaskCount).Should().Be((1m, 1));

        (await Employees(s, new GetLedEmployeeHoursQuery(ProjectId: s.OtherProjectId))).Items
            .Should().BeEmpty("the lead does not lead that project");
        (await Employees(s, new GetLedEmployeeHoursQuery(ProjectId: s.ProjectId))).TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Task_list_classifies_filters_sorts_and_pages()
    {
        var s = await SeedAsync();

        var all = await Tasks(s, new GetLedProgressTasksQuery());
        all.TotalCount.Should().Be(7);
        all.Items.Select(t => t.TaskId).Should().Equal(
            s.Tasks["t5"], s.Tasks["t1"], s.Tasks["t8"], s.Tasks["t3"], s.Tasks["t2"], s.Tasks["t9"], s.Tasks["t4"]);
        var t1 = all.Items.Single(t => t.TaskId == s.Tasks["t1"]);
        (t1.Status, t1.StatusName, t1.EstimatedHours, t1.ProjectId, t1.ObjectiveId).Should().Be(("completed", "Done", 5m, s.ProjectId, s.ModuleId));
        t1.ProjectName.Should().NotBeNullOrEmpty();
        t1.ObjectiveTitle.Should().NotBeNullOrEmpty();
        t1.Assignees.Should().ContainSingle(a => a.EmployeeId == s.AliceId && a.DisplayName == "Team Alpha");
        all.Items.Single(t => t.TaskId == s.Tasks["t2"]).Status.Should().Be("completed", "100% counts as done");
        all.Items.Single(t => t.TaskId == s.Tasks["t3"]).Status.Should().Be("inProgress");
        var t4 = all.Items.Single(t => t.TaskId == s.Tasks["t4"]);
        (t4.Status, t4.EstimatedHours, t4.DueDate).Should().Be(("notStarted", 0m, (DateOnly?)null));
        all.Items.Single(t => t.TaskId == s.Tasks["t5"]).Assignees.Should().BeEmpty();

        (await Tasks(s, new GetLedProgressTasksQuery(Status: "completed"))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[] { s.Tasks["t1"], s.Tasks["t2"], s.Tasks["t9"] });
        (await Tasks(s, new GetLedProgressTasksQuery(Status: "notStarted"))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[] { s.Tasks["t4"], s.Tasks["t5"] });
        (await Tasks(s, new GetLedProgressTasksQuery(EmployeeId: s.BobId))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[] { s.Tasks["t3"], s.Tasks["t4"] });
        (await Tasks(s, new GetLedProgressTasksQuery(EmployeeId: s.OutsiderId))).Items
            .Should().BeEmpty("an employee with no task in the led scope yields nothing");
        (await Tasks(s, new GetLedProgressTasksQuery(EmployeeId: s.AliceId))).TotalCount
            .Should().Be(3, "Alice's out-of-scope, subtask and deleted tasks never leak in");
        (await Tasks(s, new GetLedProgressTasksQuery(ObjectiveId: s.SubId))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[] { s.Tasks["t8"], s.Tasks["t9"] });
        (await Tasks(s, new GetLedProgressTasksQuery(ProjectId: s.OtherProjectId))).Items.Should().BeEmpty();
        (await Tasks(s, new GetLedProgressTasksQuery(Search: "T8 TITLE"))).Items.Select(t => t.TaskId)
            .Should().Equal(s.Tasks["t8"]);
        (await Tasks(s, new GetLedProgressTasksQuery(Search: "tp-5"))).Items.Select(t => t.TaskId)
            .Should().Equal(s.Tasks["t5"]);

        (await Tasks(s, new GetLedProgressTasksQuery(Sort: "dueDate", Direction: "asc"))).Items.Select(t => t.TaskId).Should().Equal(
            s.Tasks["t2"], s.Tasks["t3"], s.Tasks["t1"], s.Tasks["t8"], s.Tasks["t5"], s.Tasks["t4"], s.Tasks["t9"]);
        (await Tasks(s, new GetLedProgressTasksQuery(Sort: "dueDate", Direction: "desc"))).Items.Select(t => t.TaskId).Should().Equal(
            s.Tasks["t5"], s.Tasks["t8"], s.Tasks["t1"], s.Tasks["t3"], s.Tasks["t2"], s.Tasks["t4"], s.Tasks["t9"]);
        (await Tasks(s, new GetLedProgressTasksQuery(Sort: "title", Direction: "asc"))).Items.Select(t => t.TaskId).Should().Equal(
            s.Tasks["t1"], s.Tasks["t2"], s.Tasks["t3"], s.Tasks["t4"], s.Tasks["t5"], s.Tasks["t8"], s.Tasks["t9"]);
        (await Tasks(s, new GetLedProgressTasksQuery(Sort: "status", Direction: "asc"))).Items.Select(t => t.Status).Should().Equal(
            "notStarted", "notStarted", "inProgress", "inProgress", "completed", "completed", "completed");

        var page3 = await Tasks(s, new GetLedProgressTasksQuery(PageNumber: 3, PageSize: 3));
        page3.Items.Select(t => t.TaskId).Should().Equal(s.Tasks["t4"]);
        (page3.TotalCount, page3.TotalPages, page3.HasNext, page3.HasPrevious).Should().Be((7, 3, false, true));
        (await Tasks(s, new GetLedProgressTasksQuery(PageNumber: 9, PageSize: 3))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_lead_and_other_tenant_see_nothing()
    {
        var s = await SeedAsync();

        // The outsider leads only their own project, so the lead's data never shows up for them.
        var outsiderEmployees = (await Employees(s, new GetLedEmployeeHoursQuery(), s.OutsiderUserId));
        outsiderEmployees.Items.Select(i => i.EmployeeId).Should().Equal(s.AliceId);
        outsiderEmployees.Items[0].TotalHours.Should().Be(99, "only the outsider's own project counts for them");
        (await Tasks(s, new GetLedProgressTasksQuery(ProjectId: s.ProjectId), s.OutsiderUserId)).Items.Should().BeEmpty();
        (await Tasks(s, new GetLedProgressTasksQuery(ObjectiveId: s.ModuleId), s.OutsiderUserId)).Items.Should().BeEmpty();

        // Tenant predicate in the repository itself: same Module ids, another tenant -> nothing.
        await using var db = s.Db.NewContext();
        var reads = new EfLedProgressReadRepository(db);
        var ids = new[] { s.ModuleId, s.SubId };
        var otherTenant = Guid.NewGuid();
        (await reads.ListEmployeeHoursAsync(otherTenant,
            new LedEmployeeHoursFilter(ids, null, LedEmployeeHoursSort.TotalHours, true, 0, 20))).TotalCount.Should().Be(0);
        (await reads.GetUnassignedHoursAsync(otherTenant, ids, null)).TaskCount.Should().Be(0);
        (await reads.GetPeriodTotalsAsync(otherTenant, ids, LedProgressPeriod.Month(FixedToday))).TaskCount.Should().Be(0);
        (await reads.ListTasksAsync(otherTenant,
            new LedProgressTaskFilter(ids, null, null, null, LedProgressTaskSort.EstimatedHours, true, 0, 20))).TotalCount.Should().Be(0);
    }

    private static async Task<LedPeriodProgressResponse> Period(Seeded s, GetLedPeriodProgressQuery query, Guid? userId = null)
    {
        await using var db = s.Db.NewContext();
        var result = await PeriodHandler(db, s.Db.TenantId, userId ?? s.LeadUserId).Handle(query, default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    /// <summary>
    /// On top of the base seed (due: t2 22 Oct, t3 23 Oct, t1 24 Oct, t8 26 Oct, t5 31 Oct; t4/t9 undated)
    /// adds window-edge tasks in Module, all notStarted unless noted:
    ///   mon  Mon 19 Oct, no estimate, Bob         (week + month, unestimated)
    ///   sun  Sun 25 Oct, 6h, unassigned           (week + month)
    ///   prev Sun 18 Oct, 2h, Alice                (month only - the Sunday before the week)
    ///   oct1 Thu  1 Oct, 1h, Done, unassigned     (month only, completed)
    ///   nov1 Sun  1 Nov, 8h, Alice                (neither)
    ///   sep  Wed 30 Sep, 9h, Bob                  (neither)
    /// </summary>
    private static async Task<Seeded> SeedWithWindowEdgesAsync()
    {
        var s = await SeedAsync();
        await using var db = s.Db.NewContext();
        var doing = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && !st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
        var done = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
        var tasks = new Dictionary<string, Guid>(s.Tasks);
        void Add(string key, Guid status, decimal? hours, Guid? assignee, DateOnly due)
        {
            var task = s.Db.AddTask(db, s.ProjectId, s.ModuleId, status, 0, due, null, assignee, hours);
            task.ShortId = $"TP-{key}";
            task.Title = $"{key} title";
            tasks[key] = task.Id;
        }

        Add("mon", doing, null, s.BobId, new DateOnly(2026, 10, 19));
        Add("sun", doing, 6, null, new DateOnly(2026, 10, 25));
        Add("prev", doing, 2, s.AliceId, new DateOnly(2026, 10, 18));
        Add("oct1", done, 1, null, new DateOnly(2026, 10, 1));
        Add("nov1", doing, 8, s.AliceId, new DateOnly(2026, 11, 1));
        Add("sep", doing, 9, s.BobId, new DateOnly(2026, 9, 30));
        await db.SaveChangesAsync();
        return s with { Tasks = tasks };
    }

    [Fact]
    public async Task Period_totals_count_only_tasks_due_inside_the_inclusive_window()
    {
        var s = await SeedWithWindowEdgesAsync();

        var week = await Period(s, new GetLedPeriodProgressQuery("week"));
        (week.Period, week.From, week.To).Should().Be(("week", new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)));
        // t1 5h + t2 2h completed; t3 3h inProgress; mon 0h + sun 6h notStarted.
        (week.CompletedHours, week.InProgressHours, week.NotStartedHours, week.TotalHours).Should().Be((7m, 3m, 6m, 16m));
        (week.TaskCount, week.CompletedTaskCount, week.InProgressTaskCount, week.NotStartedTaskCount, week.UnestimatedTaskCount)
            .Should().Be((5, 2, 1, 2, 1));

        var month = await Period(s, new GetLedPeriodProgressQuery("MONTH"));
        (month.Period, month.From, month.To).Should().Be(("month", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        // Week plus: t8 4h inProgress, t5 7h + prev 2h notStarted, oct1 1h completed.
        (month.CompletedHours, month.InProgressHours, month.NotStartedHours, month.TotalHours).Should().Be((8m, 7m, 15m, 30m));
        (month.TaskCount, month.CompletedTaskCount, month.InProgressTaskCount, month.NotStartedTaskCount, month.UnestimatedTaskCount)
            .Should().Be((9, 3, 2, 4, 1));

        var sub = await Period(s, new GetLedPeriodProgressQuery("month", ObjectiveId: s.SubId));
        (sub.TotalHours, sub.TaskCount).Should().Be((4m, 1), "t9 in Sub is undated, t8 is due 26 Oct");

        var foreign = await Period(s, new GetLedPeriodProgressQuery("month", ProjectId: s.OtherProjectId));
        (foreign.From, foreign.To, foreign.TotalHours, foreign.TaskCount)
            .Should().Be((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 0m, 0));
    }

    [Fact]
    public async Task Period_param_windows_the_task_list()
    {
        var s = await SeedWithWindowEdgesAsync();

        (await Tasks(s, new GetLedProgressTasksQuery(Period: "week", PageSize: 100))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[] { s.Tasks["t1"], s.Tasks["t2"], s.Tasks["t3"], s.Tasks["mon"], s.Tasks["sun"] });
        (await Tasks(s, new GetLedProgressTasksQuery(Period: "Month", PageSize: 100))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[]
            {
                s.Tasks["t1"], s.Tasks["t2"], s.Tasks["t3"], s.Tasks["mon"], s.Tasks["sun"],
                s.Tasks["t8"], s.Tasks["t5"], s.Tasks["prev"], s.Tasks["oct1"],
            });
        (await Tasks(s, new GetLedProgressTasksQuery(Period: "all", PageSize: 100))).TotalCount
            .Should().Be(13, "all time keeps undated and out-of-window tasks");
        (await Tasks(s, new GetLedProgressTasksQuery(Period: "week", EmployeeId: s.AliceId))).Items.Select(t => t.TaskId)
            .Should().BeEquivalentTo(new[] { s.Tasks["t1"], s.Tasks["t2"] });
    }

    [Fact]
    public async Task Period_param_windows_employee_hours_and_unassigned_totals()
    {
        var s = await SeedWithWindowEdgesAsync();

        var week = await Employees(s, new GetLedEmployeeHoursQuery(Period: "week"));
        var alice = week.Items.Single(i => i.EmployeeId == s.AliceId);
        (alice.CompletedHours, alice.InProgressHours, alice.NotStartedHours, alice.TotalHours, alice.TaskCount)
            .Should().Be((7m, 0m, 0m, 7m, 2));
        var bob = week.Items.Single(i => i.EmployeeId == s.BobId);
        (bob.InProgressHours, bob.NotStartedHours, bob.TotalHours, bob.TaskCount).Should().Be((3m, 0m, 3m, 2));
        (week.Unassigned.NotStartedHours, week.Unassigned.TotalHours, week.Unassigned.TaskCount).Should().Be((6m, 6m, 1));

        var month = await Employees(s, new GetLedEmployeeHoursQuery(Period: "month"));
        var aliceMonth = month.Items.Single(i => i.EmployeeId == s.AliceId);
        (aliceMonth.TotalHours, aliceMonth.TaskCount).Should().Be((13m, 4), "t1, t2, t8 and the 18 Oct task count, 1 Nov does not");
        month.Items.Single(i => i.EmployeeId == s.BobId).TotalHours.Should().Be(3m, "the 30 Sep task is outside October");
        (month.Unassigned.CompletedHours, month.Unassigned.NotStartedHours, month.Unassigned.TotalHours, month.Unassigned.TaskCount)
            .Should().Be((1m, 13m, 14m, 3));

        // All time still counts undated and out-of-window tasks.
        var all = await Employees(s, new GetLedEmployeeHoursQuery());
        all.Items.Single(i => i.EmployeeId == s.AliceId).TotalHours.Should().Be(21m);
        all.Unassigned.TaskCount.Should().Be(4);
    }

    [Fact]
    public async Task Period_totals_match_the_employee_and_unassigned_split_for_single_assignee_data()
    {
        var s = await SeedWithWindowEdgesAsync();

        foreach (var period in new[] { "week", "month" })
        {
            var totals = await Period(s, new GetLedPeriodProgressQuery(period));
            var employees = await Employees(s, new GetLedEmployeeHoursQuery(Period: period));
            (employees.Items.Sum(i => i.TotalHours) + employees.Unassigned.TotalHours).Should().Be(totals.TotalHours);
            (employees.Items.Sum(i => i.TaskCount) + employees.Unassigned.TaskCount).Should().Be(totals.TaskCount);
        }
    }

    [Fact]
    public async Task Period_endpoint_command_count_does_not_grow_with_the_task_population()
    {
        var s = await SeedAsync();

        async Task<int> CountAsync()
        {
            var counter = new CountingDbCommandInterceptor();
            await using var db = s.Db.NewContext(counter);
            await PeriodHandler(db, s.Db.TenantId, s.LeadUserId).Handle(new GetLedPeriodProgressQuery("month"), default);
            return counter.Count;
        }

        var before = await CountAsync();
        await using (var db = s.Db.NewContext())
        {
            var status = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && !st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
            for (var i = 0; i < 30; i++)
                s.Db.AddTask(db, s.ProjectId, s.ModuleId, status, i % 3 * 40, FixedToday, assigneeEmployeeId: s.AliceId, estimatedHours: 1);
            await db.SaveChangesAsync();
        }

        (await CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Command_count_does_not_grow_with_the_task_population()
    {
        var s = await SeedAsync();

        async Task<int> CountAsync(Func<ApplicationDbContext, Task> run)
        {
            var counter = new CountingDbCommandInterceptor();
            await using var db = s.Db.NewContext(counter);
            await run(db);
            return counter.Count;
        }

        var before = await CountAsync(db => EmployeeHandler(db, s.Db.TenantId, s.LeadUserId).Handle(new GetLedEmployeeHoursQuery(), default));
        var beforeTasks = await CountAsync(db => TaskHandler(db, s.Db.TenantId, s.LeadUserId).Handle(new GetLedProgressTasksQuery(), default));

        await using (var db = s.Db.NewContext())
        {
            var status = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && !st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
            for (var i = 0; i < 30; i++)
                s.Db.AddTask(db, s.ProjectId, s.ModuleId, status, i % 3 * 40, null, assigneeEmployeeId: i % 2 == 0 ? s.AliceId : s.BobId, estimatedHours: 1);
            await db.SaveChangesAsync();
        }

        var after = await CountAsync(db => EmployeeHandler(db, s.Db.TenantId, s.LeadUserId).Handle(new GetLedEmployeeHoursQuery(), default));
        var afterTasks = await CountAsync(db => TaskHandler(db, s.Db.TenantId, s.LeadUserId).Handle(new GetLedProgressTasksQuery(), default));

        after.Should().Be(before);
        afterTasks.Should().Be(beforeTasks);
    }

    private static async Task<LedWeeklyCompletionResponse> Weekly(Seeded s, GetLedWeeklyCompletionQuery query, Guid? userId = null)
    {
        await using var db = s.Db.NewContext();
        var result = await WeeklyHandler(db, s.Db.TenantId, userId ?? s.LeadUserId).Handle(query, default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 12, int minute = 0, int second = 0)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    /// <summary>
    /// On top of the base seed (done but undated: t1 5h, t2 2h, t9 1h in Sub) adds tasks with a
    /// CompletedAt. The 10-week span is Mon 17 Aug .. Sun 25 Oct 2026 (week 9 = current).
    ///   cNow     Done 3h         21 Oct 10:00        week 9
    ///   cMonday  Doing 100% 2h   19 Oct 00:00:00     week 9 (first instant of the week)
    ///   cSunday  Done 4h         18 Oct 23:59:59     week 8 (last instant of the previous week)
    ///   cFirst   Done no estim.  17 Aug 00:00        week 0 (0 h, still counted)
    ///   cBefore  Done 9h         16 Aug 23:00        before the span - not counted anywhere
    ///   reopened Doing 0% 6h     20 Oct              completed then reopened: not done -> excluded
    ///   cSub     Done 1.5h (Sub) 30 Sep              week 6
    ///   subtask  Done 50h        21 Oct, child of cNow - subtasks never count
    ///   foreign  Done 99h        21 Oct, in the outsider's project - out of the lead's scope
    /// </summary>
    private static async Task<Seeded> SeedWithCompletionsAsync()
    {
        var s = await SeedAsync();
        await using var db = s.Db.NewContext();
        var doing = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && !st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
        var done = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
        var tasks = new Dictionary<string, Guid>(s.Tasks);
        WorkTask Add(string key, Guid objective, Guid status, int progress, decimal? hours, DateTimeOffset completedAt,
            Guid? parent = null, Guid? project = null)
        {
            var task = s.Db.AddTask(db, project ?? s.ProjectId, objective, status, progress, null, parent, s.AliceId, hours);
            task.ShortId = $"TP-{key}";
            task.Title = $"{key} title";
            task.CompletedAt = completedAt;
            tasks[key] = task.Id;
            return task;
        }

        var now = Add("cNow", s.ModuleId, done, 0, 3, Utc(2026, 10, 21, 10));
        Add("cMonday", s.ModuleId, doing, 100, 2, Utc(2026, 10, 19, 0));
        Add("cSunday", s.ModuleId, done, 0, 4, Utc(2026, 10, 18, 23, 59, 59));
        Add("cFirst", s.ModuleId, done, 0, null, Utc(2026, 8, 17, 0));
        Add("cBefore", s.ModuleId, done, 0, 9, Utc(2026, 8, 16, 23));
        Add("reopened", s.ModuleId, doing, 0, 6, Utc(2026, 10, 20));
        Add("cSub", s.SubId, done, 0, 1.5m, Utc(2026, 9, 30));
        Add("subtask-done", s.ModuleId, done, 0, 50, Utc(2026, 10, 21), parent: now.Id);

        var otherRoot = await db.Objectives.Where(o => o.ProjectId == s.OtherProjectId && o.ParentObjectiveId == null).Select(o => o.Id).FirstAsync();
        var otherDone = s.Db.AddStatus(db, s.OtherProjectId, true);
        Add("foreign-done", otherRoot, otherDone, 0, 99, Utc(2026, 10, 21), project: s.OtherProjectId);

        await db.SaveChangesAsync();
        return s with { Tasks = tasks };
    }

    [Fact]
    public async Task Weekly_completion_buckets_done_tasks_by_completed_at_utc_week()
    {
        var s = await SeedWithCompletionsAsync();

        var r = await Weekly(s, new GetLedWeeklyCompletionQuery());

        r.Weeks.Should().HaveCount(10);
        (r.Weeks[0].WeekStart, r.Weeks[0].WeekEnd).Should().Be((new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 23)));
        (r.Weeks[^1].WeekStart, r.Weeks[^1].WeekEnd).Should().Be((new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)));
        r.Weeks.Select(w => (w.CompletedHours, w.CompletedTaskCount)).Should().Equal(
            (0m, 1),   // 17 Aug: cFirst, no estimate
            (0m, 0), (0m, 0), (0m, 0), (0m, 0), (0m, 0),
            (1.5m, 1), // 28 Sep: cSub
            (0m, 0),
            (4m, 1),   // 12 Oct: cSunday 23:59:59
            (5m, 2));  // 19 Oct: cNow + cMonday 00:00 - not reopened, subtask or the foreign task

        (r.UndatedCompletedHours, r.UndatedCompletedTaskCount).Should().Be((8m, 3), "t1, t2, t9 are done with no CompletedAt");
    }

    [Fact]
    public async Task Weekly_completion_allocation_matches_led_progress_and_follows_the_filters()
    {
        var s = await SeedWithCompletionsAsync();

        LedWorkProgressResponse ledProgress;
        await using (var db = s.Db.NewContext())
        {
            var handler = new GetLedWorkProgressQueryHandler(
                new StubUser(s.Db.TenantId, s.LeadUserId), new FixedClock(), new EfEmployeeRepository(db), WorkLeadership(db),
                new EfWorkTaskRepository(db), new EfModuleHourSummaryRepository(db), new EfProjectRepository(db));
            ledProgress = (await handler.Handle(new GetLedWorkProgressQuery(), default)).Value!;
        }

        var all = await Weekly(s, new GetLedWeeklyCompletionQuery());
        all.AllocatedHours.Should().Be(ledProgress.Hours.AllocatedHours).And.Be(100m);

        var sub = await Weekly(s, new GetLedWeeklyCompletionQuery(ObjectiveId: s.SubId));
        sub.AllocatedHours.Should().Be(40m);
        sub.Weeks.Sum(w => w.CompletedHours).Should().Be(1.5m);
        (sub.UndatedCompletedHours, sub.UndatedCompletedTaskCount).Should().Be((1m, 1));

        var project = await Weekly(s, new GetLedWeeklyCompletionQuery(ProjectId: s.ProjectId, Weeks: 2));
        (project.Weeks.Count, project.AllocatedHours).Should().Be((2, 100m));
        project.Weeks.Select(w => w.CompletedHours).Should().Equal(4m, 5m);

        var foreign = await Weekly(s, new GetLedWeeklyCompletionQuery(ProjectId: s.OtherProjectId));
        foreign.Weeks.Should().HaveCount(10).And.OnlyContain(w => w.CompletedHours == 0 && w.CompletedTaskCount == 0);
        (foreign.AllocatedHours, foreign.UndatedCompletedTaskCount).Should().Be((0m, 0));
    }

    [Fact]
    public async Task Weekly_completion_never_crosses_scope_or_tenant()
    {
        var s = await SeedWithCompletionsAsync();

        // The outsider leads only their own project: just the foreign 99 h task, this week.
        var outsider = await Weekly(s, new GetLedWeeklyCompletionQuery(), s.OutsiderUserId);
        (outsider.Weeks[^1].CompletedHours, outsider.Weeks[^1].CompletedTaskCount).Should().Be((99m, 1));
        outsider.Weeks.Take(9).Sum(w => w.CompletedTaskCount).Should().Be(0);
        outsider.UndatedCompletedTaskCount.Should().Be(0);

        // Tenant predicate in the repository itself: same Module ids, another tenant -> nothing.
        await using var db = s.Db.NewContext();
        var rows = await new EfLedProgressReadRepository(db).GetCompletionsByDayAsync(
            Guid.NewGuid(), new[] { s.ModuleId, s.SubId },
            LedCompletionWeeks.Span(LedCompletionWeeks.Windows(FixedToday, 10)));
        (rows.Days.Count, rows.UndatedHours, rows.UndatedTaskCount).Should().Be((0, 0m, 0));
    }

    [Fact]
    public async Task Weekly_completion_command_count_does_not_grow_with_the_task_population()
    {
        var s = await SeedWithCompletionsAsync();

        async Task<int> CountAsync()
        {
            var counter = new CountingDbCommandInterceptor();
            await using var db = s.Db.NewContext(counter);
            await WeeklyHandler(db, s.Db.TenantId, s.LeadUserId).Handle(new GetLedWeeklyCompletionQuery(), default);
            return counter.Count;
        }

        var before = await CountAsync();
        await using (var db = s.Db.NewContext())
        {
            var done = await db.TaskStatuses.Where(st => st.ProjectId == s.ProjectId && st.MarksTaskComplete).Select(st => st.Id).FirstAsync();
            for (var i = 0; i < 30; i++)
            {
                var task = s.Db.AddTask(db, s.ProjectId, s.ModuleId, done, 0, null, assigneeEmployeeId: s.AliceId, estimatedHours: 1);
                task.CompletedAt = i % 3 == 0 ? null : Utc(2026, 10, 21).AddDays(-i * 2);
            }
            await db.SaveChangesAsync();
        }

        (await CountAsync()).Should().Be(before);
    }
}
