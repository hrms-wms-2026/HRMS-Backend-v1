using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using ProductionPlacement = ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.2 direct create: the series, its attendees and its first occurrences in one transaction.</summary>
public sealed class MeetingCreateIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);

    private static MeetingSeriesFactory Factory(ApplicationDbContext db) => new(
        new EfMeetingRepository(db), new EfMeetingOccurrenceWriter(db), new MeetingTaskDefaults(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db)),
        new EfProjectRepository(db), new EfEmployeeRepository(db), new IcalNetMeetingRecurrence(), new UnitOfWork(db), new FixedClock(Now),
        new MeetingTestServices(db, new FixedClock(Now)).Materializer);

    private static CreateMeetingRequest Request(MeetingPlacement at, IReadOnlyList<Guid> people, string? rule = null,
        IReadOnlyList<string>? guests = null)
        => new(at.ProjectId, at.ObjectiveId, "Delivery sync", "Agenda", "Room 4", false, Now.AddDays(1), 30, false,
            "Asia/Colombo", rule, people, guests ?? [], null);

    private static async Task<MeetingCreatedResponse> CreateAsync(MyTeamDb h, MeetingPlacement at, CreateMeetingRequest request)
    {
        await using var db = h.NewContext();
        var factory = Factory(db);
        var result = await new UnitOfWork(db).ExecuteInTransactionAsync(ct => factory.CreateDirectAsync(
            h.TenantId, at.OrganizerEmployeeId, Guid.NewGuid(), new ProductionPlacement(at.ProjectId, at.ObjectiveId, false, true),
            request, MeetingCreationPaths.Direct, approvalRequestId: null, ct));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task OneOff_CreatesOneSeriesOneOccurrenceAndItsAttendees()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        Guid attendee;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            attendee = h.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        var created = await CreateAsync(h, at, Request(at, [attendee], guests: ["Client@Example.com", "client@example.com"]));

        Assert.Equal("created", created.Outcome);
        await using var check = h.NewContext();
        var task = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).SingleAsync(t => t.Id == created.FirstOccurrenceTaskId);
        var project = await check.Projects.SingleAsync(p => p.Id == at.ProjectId);
        Assert.Equal(WorkTaskKinds.Meeting, task.TaskKind);
        Assert.Equal($"{project.Identifier}-M1", task.ShortId);
        Assert.Equal(TaskCategorySystemKeys.Meeting, (await check.TaskCategories.SingleAsync(c => c.Id == task.CategoryId)).SystemKey);
        Assert.Equal(1, await check.MeetingDetails.CountAsync(d => d.SeriesId == created.SeriesId));
        var participants = await check.CalendarEventParticipants.Where(p => p.MeetingSeriesId == created.SeriesId).ToListAsync();
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, participants.Single(p => p.EmployeeId == at.OrganizerEmployeeId).ResponseStatus);
        Assert.Equal(CalendarEventParticipantStatuses.Pending, participants.Single(p => p.EmployeeId == attendee).ResponseStatus);
        Assert.Single(await check.CalendarEventGuests.Where(g => g.MeetingSeriesId == created.SeriesId).ToListAsync());
    }

    [Fact]
    public async Task Daily_MaterializesTheNextSevenDaysOnly()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            await db.SaveChangesAsync();
        }

        var created = await CreateAsync(h, at, Request(at, [], rule: "FREQ=DAILY"));

        await using var check = h.NewContext();
        var starts = await check.MeetingDetails.Where(d => d.SeriesId == created.SeriesId).Select(d => d.OriginalStart).ToListAsync();
        Assert.InRange(starts.Count, 6, 7);
        Assert.All(starts, s => Assert.True(s < Now.AddDays(7), $"{s:O} is beyond the 7-day window"));
        Assert.Contains(Now.AddDays(1), starts);
    }

    [Fact]
    public async Task InactiveAndUnknownParticipants_AreSkippedAndReported()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        Guid gone;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            var leaver = h.AddEmployee(db);
            leaver.EmploymentStatusId = EmploymentStatusIds.Terminated;
            gone = leaver.Id;
            await db.SaveChangesAsync();
        }
        var unknown = Guid.NewGuid();

        var created = await CreateAsync(h, at, Request(at, [gone, unknown]));

        Assert.Equal([(gone, "inactive"), (unknown, "not_found")], created.Skipped.Select(s => (s.EmployeeId, s.Reason)).ToList());
        await using var check = h.NewContext();
        Assert.Equal([at.OrganizerEmployeeId],
            await check.CalendarEventParticipants.Where(p => p.MeetingSeriesId == created.SeriesId).Select(p => p.EmployeeId).ToListAsync());
    }

    [Fact]
    public async Task OrdinaryTaskQueries_DoNotShowTheMeeting()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            await db.SaveChangesAsync();
        }

        await CreateAsync(h, at, Request(at, []));

        await using var check = h.NewTenantContext();
        Assert.Empty(await check.WorkTasks.ToListAsync());
    }

    private sealed class FixedClock(DateTimeOffset now) : ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }
}
