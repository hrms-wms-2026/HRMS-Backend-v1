using System.Reflection;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Team week planned vs actual (2026-11-02 design match, section 7.4): the handler on the real read repositories.</summary>
public sealed class TeamScheduleWeekIntegrationTests
{
    // Wednesday 2026-10-28 12:00 UTC; everyone here reads in the seeded legal entity's UTC zone.
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
        public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
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

    /// <summary>Answers only the two calls the handler makes; anything else throws so a new dependency is noticed.</summary>
    private class Fake : DispatchProxy
    {
        public Func<string, object?[], object?> Answer = (name, _) => throw new NotSupportedException(name);

        protected override object? Invoke(MethodInfo? method, object?[]? args) => Answer(method!.Name, args ?? []);

        public static T Create<T>(Func<string, object?[], object?> answer)
        {
            var proxy = DispatchProxy.Create<T, Fake>();
            ((Fake)(object)proxy!).Answer = answer;
            return proxy;
        }
    }

    private static TaskClockingSession AddSession(MyTeamDb h, ApplicationDbContext db, Guid employeeId, Guid taskId, int day, int fromHour, int toHour)
    {
        var session = new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, TaskId = taskId,
            ClockInAt = Monday.AddDays(day).AddHours(fromHour), ClockOutAt = Monday.AddDays(day).AddHours(toHour),
        };
        db.TaskClockingSessions.Add(session);
        return session;
    }

    private static void AddBlock(MyTeamDb h, ApplicationDbContext db, Guid employeeId, Guid taskId, int day, int fromHour, int toHour)
        => db.PlannedTaskBlocks.Add(new PlannedTaskBlock
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, TaskId = taskId,
            StartAt = Monday.AddDays(day).AddHours(fromHour), EndAt = Monday.AddDays(day).AddHours(toHour), CreatedById = employeeId,
        });

    private static async Task<(TeamScheduleWeekResponse Week, int Commands)> RunAsync(MyTeamDb h, Guid managerId, IReadOnlyList<Guid> reports)
    {
        var counter = new CountingDbCommandInterceptor();
        await using var db = h.NewContext(counter);
        var userId = Guid.NewGuid();
        var identity = Fake.Create<ICallerIdentityResolver>((name, _) => name switch
        {
            nameof(ICallerIdentityResolver.ResolveCallerEmployeeIdAsync) => Task.FromResult<Guid?>(managerId),
            _ => throw new NotSupportedException(name),
        });
        var employees = Fake.Create<IEmployeeRepository>((name, args) => name switch
        {
            nameof(IEmployeeRepository.ListDirectReportEmployeeIdsAsync) => Task.FromResult<IReadOnlyList<Guid>>(
                ((IReadOnlyCollection<Guid>)args[1]!).Contains(managerId) ? reports.ToList() : []),
            _ => throw new NotSupportedException(name),
        });
        var handler = new GetTeamScheduleWeekQueryHandler(new StubUser(h.TenantId, userId), identity, employees,
            new EfTeamTimeReadRepository(db), new EfMeetingRepository(db), new FixedClock(),
            Options.Create(new PlannerOptions { Enabled = true }));

        var result = await handler.Handle(new GetTeamScheduleWeekQuery(null), default);
        Assert.True(result.IsSuccess, result.Error);
        return (result.Value!, counter.Count);
    }

    [Fact]
    public async Task Team_week_reads_blocks_sessions_and_meetings_once_per_window()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid managerId, taskId;
        var reports = new List<Guid>();
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            managerId = at.OrganizerEmployeeId;
            taskId = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null).Id;
            reports.Add(h.AddEmployee(db).Id);
            await db.SaveChangesAsync();
            AddBlock(h, db, reports[0], taskId, 0, 9, 11);
            AddSession(h, db, reports[0], taskId, 0, 10, 11);      // Monday, inside the block
            AddSession(h, db, reports[0], taskId, 1, 9, 10);       // Tuesday, no plan
            AddSession(h, db, reports[0], taskId, -7, 9, 10);      // last Monday, inside the previous span
            await db.SaveChangesAsync();
        }

        var (one, oneCommands) = await RunAsync(h, managerId, reports);

        Assert.Equal(new DateOnly(2026, 10, 26), one.WeekStart);
        Assert.Equal((60, 60), (one.Days[0].ClockedMinutes, one.Days[1].ClockedMinutes));
        Assert.Equal((120, 60, 120, 60, 50.0m),
            (one.Totals.PlannedMinutes, one.Totals.ScheduledMinutes, one.Totals.ClockedMinutes, one.Totals.UnscheduledMinutes,
                one.Totals.CoveragePercent));
        Assert.Equal(60, one.Previous.ClockedMinutes);

        await using (var db = h.NewContext())
        {
            for (var i = 0; i < 4; i++)
            {
                var id = h.AddEmployee(db).Id;
                reports.Add(id);
                await db.SaveChangesAsync();
                AddBlock(h, db, id, taskId, i, 9, 10);
                AddSession(h, db, id, taskId, i, 9, 10);
            }
            await db.SaveChangesAsync();
        }

        var (five, fiveCommands) = await RunAsync(h, managerId, reports);

        Assert.Equal(5, five.Members.Count);
        Assert.Equal(oneCommands, fiveCommands);   // batched: the command count does not grow with the team
    }
}
