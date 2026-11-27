using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Diagnostics;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using EfCalendarEventRepository = ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfCalendarEventRepository;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.Calendar;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;
using Xunit.Abstractions;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Integration.Performance;

/// <summary>
/// Improvements task 14: the four read paths that used to query inside a loop now run a fixed number of
/// SQL commands however many items they return. Counted on real SQL Server with the task 06 request
/// counter (<see cref="SqlCommandMetricsInterceptor"/>), once with a small and once with a larger data set;
/// the two counts must be equal. Requires Docker (Testcontainers SQL Server).
/// </summary>
public sealed class QueryInLoopBudgetIntegrationTests(ITestOutputHelper output)
{
    private const int Small = 1;
    private const int Large = 6;
    private const string ApprovePermission = "leave:approve";

    private sealed class StubCurrentUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private sealed class AllowRead : IEmployeeReadAccessGuard
    {
        public Task<Result<EmployeeListItemResponse>> EnsureCanRead(Guid tenantId, Guid employeeId, CancellationToken ct = default)
            => Task.FromResult(Result<EmployeeListItemResponse>.Success(null!));
    }

    private sealed class NoPermissions : IPermissionResolver
    {
        public Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, Guid? activeLegalEntityId, CancellationToken ct = default)
            => Task.FromResult(new List<string>());
    }

    /// <summary>A read context with the task 06 counter attached.</summary>
    private static (ApplicationDbContext Db, RequestSqlMetrics Metrics) Counted(MyTeamDb helper)
    {
        var metrics = new RequestSqlMetrics();
        return (helper.NewTenantContext(counter: new SqlCommandMetricsInterceptor(metrics)), metrics);
    }

    private void Report(string path, int small, int large)
    {
        output.WriteLine($"{path}: {small} SQL commands with {Small} item(s), {large} with {Large}");
        large.Should().Be(small, $"{path} must run a fixed number of queries ({small} with {Small} item(s), {large} with {Large})");
    }

    // --- calendar ----------------------------------------------------------------------------

    private sealed record CalendarWorld(MyTeamDb Helper, Guid CallerUserId, Guid CallerEmployeeId);

    /// <summary>n one-off events and n weekly series, each made by a different organizer, with the caller
    /// and two (one for a series) other participants each.</summary>
    private static async Task<CalendarWorld> SeedCalendarAsync(int n)
    {
        var helper = await MyTeamDb.CreateAsync();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(9), TimeSpan.Zero);
        var creators = new List<(Guid EventId, Guid CreatorUserId)>();
        Guid callerUser, callerEmployee;
        await using (var db = helper.NewContext())
        {
            var caller = helper.AddEmployee(db);
            (callerUser, callerEmployee) = (caller.UserId, caller.Id);
            for (var i = 0; i < n; i++)
            {
                var organizer = helper.AddEmployee(db);
                var oneOff = AddEvent(db, helper.TenantId, start.AddHours(i), recurring: false,
                    caller.Id, helper.AddEmployee(db).Id, helper.AddEmployee(db).Id);
                var series = AddEvent(db, helper.TenantId, start.AddDays(1).AddHours(i), recurring: true,
                    caller.Id, helper.AddEmployee(db).Id);
                creators.Add((oneOff, organizer.UserId));
                creators.Add((series, organizer.UserId));
            }
            await db.SaveChangesAsync();
        }

        // The audit interceptor stamps CreatedById on insert; give each event its own organizer afterwards.
        await using (var db = helper.NewTenantContext())
        {
            foreach (var (eventId, creatorUserId) in creators)
                await db.PersonalCalendarEvents.Where(e => e.Id == eventId)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedById, creatorUserId));
        }

        return new CalendarWorld(helper, callerUser, callerEmployee);
    }

    private static Guid AddEvent(ApplicationDbContext db, Guid tenantId, DateTimeOffset start, bool recurring, params Guid[] participantIds)
    {
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Title = recurring ? "Weekly sync" : "Review",
            StartDate = start, EndDate = start.AddMinutes(30), SourceType = CalendarEventSourceTypes.Manual,
            Recurrence = recurring ? CalendarRecurrences.Weekly : CalendarRecurrences.None,
            RecurrenceRule = recurring ? "FREQ=WEEKLY" : null,
        };
        db.PersonalCalendarEvents.Add(calendarEvent);
        foreach (var employeeId in participantIds)
            db.CalendarEventParticipants.Add(new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventId = calendarEvent.Id, EmployeeId = employeeId,
                ResponseStatus = CalendarEventParticipantStatuses.Pending,
            });
        return calendarEvent.Id;
    }

    private static async Task<(int Commands, int Items)> CountCalendarAsync(int n)
    {
        var w = await SeedCalendarAsync(n);
        var (db, metrics) = Counted(w.Helper);
        await using (db)
        {
            var handler = new GetCalendarEventsQueryHandler(
                new StubCurrentUser(w.Helper.TenantId, w.CallerUserId), new EfEmployeeRepository(db),
                new EfCalendarEventRepository(db), new IcalNetRecurrenceExpander());
            var from = DateTimeOffset.UtcNow;
            var result = await handler.Handle(new GetCalendarEventsQuery(from, from.AddDays(14)), CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.Events.Should().OnlyContain(e => e.OrganizerName != null && e.Participants!.All(p => p.EmployeeName != "Unknown"));
            return (metrics.CommandCount, result.Value.Events.Count);
        }
    }

    [Fact]
    public async Task Calendar_events_run_a_fixed_number_of_queries()
    {
        var small = await CountCalendarAsync(Small);
        var large = await CountCalendarAsync(Large);

        // One one-off event plus two weekly occurrences per series in a 14-day window.
        small.Items.Should().Be(Small * 3);
        large.Items.Should().Be(Large * 3);
        Report("GET /calendar (GetCalendarEventsQueryHandler)", small.Commands, large.Commands);
    }

    private static async Task<(int Commands, int Items)> CountUpcomingAsync(int n)
    {
        var w = await SeedCalendarAsync(n);
        var (db, metrics) = Counted(w.Helper);
        await using (db)
        {
            var clock = new SystemDateTimeProvider();
            var handler = new GetEmployeeUpcomingQueryHandler(
                new AllowRead(), new EfEmployeeRepository(db), new EfCalendarEventRepository(db), new IcalNetRecurrenceExpander(),
                new EfLeaveRequestRepository(db, clock), new EfReleaseCalendarRepository(db),
                new StubCurrentUser(w.Helper.TenantId, w.CallerUserId), clock);
            var result = await handler.Handle(
                new GetEmployeeUpcomingQuery(w.CallerEmployeeId, 14, GetEmployeeUpcomingQueryHandler.MaxLimit), CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.Items.Should().OnlyContain(i => i.Participants!.Count > 0 && !i.Participants!.Contains("Unknown"));
            return (metrics.CommandCount, result.Value.Items.Count);
        }
    }

    [Fact]
    public async Task Employee_upcoming_runs_a_fixed_number_of_queries()
    {
        var small = await CountUpcomingAsync(Small);
        var large = await CountUpcomingAsync(Large);

        small.Items.Should().Be(Small * 3);
        large.Items.Should().Be(Large * 3);
        Report("GET /employees/{id}/upcoming (GetEmployeeUpcomingQueryHandler)", small.Commands, large.Commands);
    }

    // --- task access -------------------------------------------------------------------------

    /// <summary>n projects led by someone else; the caller is a member of one module in each, and each
    /// project has a public task in that module and an assignees-only task assigned to the caller.</summary>
    private static async Task<(MyTeamDb Helper, Guid CallerUserId, Guid CallerEmployeeId)> SeedTasksAsync(int n)
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var caller = helper.AddEmployee(db);
        for (var i = 0; i < n; i++)
        {
            var lead = helper.AddEmployee(db);
            var (projectId, rootId) = helper.AddProject(db, lead.Id);
            var moduleId = helper.AddModule(db, projectId, rootId, lead.Id);
            helper.AddMember(db, projectId, moduleId, caller.Id);
            var status = helper.AddStatus(db, projectId, marksComplete: false);
            helper.AddTask(db, projectId, moduleId, status, 0, null);
            var privateTask = helper.AddTask(db, projectId, moduleId, status, 0, null, assigneeEmployeeId: caller.Id);
            privateTask.VisibilityScope = WorkTaskVisibilityScopes.Assignees;
        }
        await db.SaveChangesAsync();
        return (helper, caller.UserId, caller.Id);
    }

    private static TaskAccessResolver TaskAccess(ApplicationDbContext db)
    {
        var commonEmployees = new ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository(db);
        var membership = new ONEVO.Application.Features.WorkManagement.Objectives.Services.MilestoneMembershipCoordinator(
            commonEmployees, new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
            new EfProjectMemberInvitationRepository(db),
            new CallerIdentityResolver(commonEmployees, new ONEVO.Infrastructure.Persistence.Repositories.EfEntityAssetRepository(db)),
            new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()));
        return new TaskAccessResolver(
            new CallerIdentityResolver(commonEmployees, new ONEVO.Infrastructure.Persistence.Repositories.EfEntityAssetRepository(db)), new EfWorkTaskRepository(db),
            new EfProjectRepository(db), new EfProjectMemberRepository(db), new NoPermissions(),
            new WorkHierarchyService(new EfObjectiveRepository(db), membership));
    }

    private static async Task<(int Resolve, int Filter, int Visible)> CountTaskAccessAsync(int n)
    {
        var (helper, callerUser, callerEmployee) = await SeedTasksAsync(n);
        IReadOnlyList<WorkTask> tasks;
        await using (var db = helper.NewTenantContext())
            tasks = await db.WorkTasks.AsNoTracking().Where(t => t.TenantId == helper.TenantId).ToListAsync();

        int resolveCommands, filterCommands, visible;
        var (resolveDb, resolveMetrics) = Counted(helper);
        await using (resolveDb)
        {
            var result = await TaskAccess(resolveDb).ResolveViewableTasksAsync(helper.TenantId, callerUser, tasks);
            (resolveCommands, visible) = (resolveMetrics.CommandCount, result.Count);
        }

        var (filterDb, filterMetrics) = Counted(helper);
        await using (filterDb)
        {
            var result = await TaskAccess(filterDb).FilterViewableTasksAsync(helper.TenantId, callerUser, callerEmployee, tasks);
            result.Should().HaveCount(tasks.Count);
            filterCommands = filterMetrics.CommandCount;
        }

        return (resolveCommands, filterCommands, visible);
    }

    [Fact]
    public async Task Task_access_runs_a_fixed_number_of_queries()
    {
        var small = await CountTaskAccessAsync(Small);
        var large = await CountTaskAccessAsync(Large);

        small.Visible.Should().Be(Small * 2);
        large.Visible.Should().Be(Large * 2);
        using (new FluentAssertions.Execution.AssertionScope())
        {
            Report("TaskAccessResolver.ResolveViewableTasksAsync", small.Resolve, large.Resolve);
            Report("TaskAccessResolver.FilterViewableTasksAsync", small.Filter, large.Filter);
        }
    }

    // --- approver routing --------------------------------------------------------------------

    /// <summary>A subject with n backup coverage owners who lack the approve permission, and a reporting
    /// chain of n managers where only the top one holds it - so every coverage record and every ancestor is
    /// checked before the route resolves to the top manager.</summary>
    private static async Task<(int Commands, EmployeeApprovalRoute Route, Guid TopManagerUserId)> CountApproverAsync(int n)
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid subjectId, topManagerUser;
        await using (var db = helper.NewContext())
        {
            var subject = helper.AddEmployee(db);
            subjectId = subject.Id;
            var subjectPosition = helper.AddPositionHeldBy(db, subject.Id);
            for (var i = 0; i < n; i++)
            {
                var backup = helper.AddEmployee(db);
                helper.AddCoverage(db, helper.AddPositionHeldBy(db, backup.Id), ManagementCoverageRecord.TargetPosition,
                    coveredPositionId: subjectPosition);
                // One owner per order: the active (covered position, order) pair is unique.
                db.ManagementCoverageRecords.Local.Last().OwnerOrder = i + 1;
            }

            var managers = Enumerable.Range(0, n).Select(_ => helper.AddEmployee(db)).ToList();
            foreach (var manager in managers)
                helper.AddPositionHeldBy(db, manager.Id);
            topManagerUser = managers[^1].UserId;
            helper.GrantPermission(db, topManagerUser, ApprovePermission);
            await db.SaveChangesAsync();

            var assignmentIds = await db.PositionAssignments.AsNoTracking()
                .Where(pa => pa.TenantId == helper.TenantId)
                .ToDictionaryAsync(pa => pa.EmployeeId, pa => pa.Id);
            for (var depth = 1; depth <= n; depth++)
                db.EmployeeHierarchyClosures.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
                {
                    TenantId = helper.TenantId, AncestorEmployeeId = managers[depth - 1].Id, DescendantEmployeeId = subject.Id,
                    Depth = depth, SourcePositionAssignmentId = assignmentIds[subject.Id], GeneratedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var (read, metrics) = Counted(helper);
        await using (read)
        {
            var resolver = new EmployeeAuthorityResolver(
                new StubCurrentUser(helper.TenantId, Guid.NewGuid()), new SystemDateTimeProvider(), new EfEmployeeRepository(read),
                PositionAssignmentRepositoryTestSupport.CreateRepository(read), new EfPositionRepository(read),
                PositionAssignmentRepositoryTestSupport.CreateClosureRepository(read), new EfDepartmentRepository(read),
                new EfPermissionRepository(read));
            var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                subjectId, helper.LegalEntityId, ApprovePermission, EmployeeAuthorityPurpose.TimeOffApproval));
            result.IsSuccess.Should().BeTrue(result.Error);
            return (metrics.CommandCount, result.Value!, topManagerUser);
        }
    }

    [Fact]
    public async Task Approver_routing_runs_a_fixed_number_of_queries()
    {
        var small = await CountApproverAsync(Small);
        var large = await CountApproverAsync(Large);

        small.Route.ApproverUserId.Should().Be(small.TopManagerUserId);
        large.Route.ApproverUserId.Should().Be(large.TopManagerUserId);
        large.Route.Source.Should().Be(EmployeeApprovalRouteSource.ReportingLine);
        Report("EmployeeAuthorityResolver.ResolveApproverAsync", small.Commands, large.Commands);
    }
}
