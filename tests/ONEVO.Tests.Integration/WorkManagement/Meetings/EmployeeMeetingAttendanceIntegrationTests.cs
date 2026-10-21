using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// Plan D Task 4 (spec D16, D17): the reporting-line attendance view on the employee profile. The employee
/// themself sees their own rows with the meeting titles; everyone above them in their <em>current</em> reporting
/// line sees the same rows, and a private meeting is masked for them. Nobody else sees anything - not HR, not
/// the project owner, not a manager who left the line.
/// </summary>
public sealed class EmployeeMeetingAttendanceIntegrationTests
{
    /// <summary>After the third occurrence, so every materialized occurrence has ended.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 6, 0, 0, TimeSpan.Zero);

    private sealed class World
    {
        public MyTeamDb H = null!;
        public Guid Organizer;
        public Guid Subject;
        public Guid Manager;
        public Guid SkipLevelManager;
        public Guid FormerManager;
        public Guid Outsider;
        public Guid SeriesId;
        public IReadOnlyList<Guid> Tasks = [];
    }

    private static async Task<World> SeedAsync()
    {
        var w = await MeetingAttendanceWorld.SeedAsync();
        var world = new World { H = w.H, Organizer = w.Organizer, Subject = w.Attendee, SeriesId = w.SeriesId, Tasks = w.Tasks };

        await using (var db = w.H.NewContext())
        {
            var manager = w.H.AddEmployee(db);
            var skipLevel = w.H.AddEmployee(db);
            var former = w.H.AddEmployee(db);
            var outsider = w.H.AddEmployee(db);

            // The current reporting line: skip level -> manager -> subject.
            db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
            {
                TenantId = w.H.TenantId, AncestorEmployeeId = manager.Id, DescendantEmployeeId = w.Attendee, Depth = 1,
                SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
            });
            db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
            {
                TenantId = w.H.TenantId, AncestorEmployeeId = skipLevel.Id, DescendantEmployeeId = w.Attendee, Depth = 2,
                SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
            });
            db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
            {
                TenantId = w.H.TenantId, AncestorEmployeeId = skipLevel.Id, DescendantEmployeeId = manager.Id, Depth = 1,
                SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
            });

            // The former manager still manages somebody else, so they are a manager in this tenant - but the
            // closure no longer makes them an ancestor of the subject: the reporting manager changed.
            var theirOwnReport = w.H.AddEmployee(db);
            db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
            {
                TenantId = w.H.TenantId, AncestorEmployeeId = former.Id, DescendantEmployeeId = theirOwnReport.Id, Depth = 1,
                SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            world.Manager = manager.Id;
            world.SkipLevelManager = skipLevel.Id;
            world.FormerManager = former.Id;
            world.Outsider = outsider.Id;
        }

        // The first occurrence settles: the subject joined twice (15 minutes), so its outcome is "joined".
        var stub = new SessionsStub
        {
            Answer = (_, _) =>
            [
                Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 10), Rec("a", w.AttendeeEmail, Week(0).AddMinutes(5), 10)),
            ],
        };
        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) })
            await SyncAsync(w, stub, End(0) + after);

        return world;
    }

    private static EmployeeMeetingAttendanceService Service(ApplicationDbContext db, DateTimeOffset now)
        => new(new EfMeetingRepository(db), new EfMeetingAttendanceRepository(db),
            new CommonEfEmployeeRepository(db), new MeetingTestClock(now));

    private static async Task<Result<EmployeeMeetingAttendanceResponse>> ReadAsync(World w, Guid caller, Guid subject,
        DateTimeOffset? from = null, DateTimeOffset? to = null, DateTimeOffset? now = null)
    {
        await using var db = w.H.NewContext();
        return await Service(db, now ?? Now).GetAsync(w.H.TenantId, caller, subject, from, to, CancellationToken.None);
    }

    [Fact]
    public async Task Self_SeesOwnRows_WithTitles()
    {
        var w = await SeedAsync();

        var result = await ReadAsync(w, w.Subject, w.Subject);

        Assert.True(result.IsSuccess, result.Error);
        var rows = result.Value!.Items;
        Assert.Equal(3, rows.Count);
        // Newest first.
        Assert.Equal(new[] { Week(2), Week(1), Week(0) }, rows.Select(r => r.StartAt));
        Assert.All(rows, r => Assert.Equal("Weekly sync", r.Title));
        // The settled occurrence: the subject joined for 15 minutes.
        var first = rows.Single(r => r.StartAt == Week(0));
        Assert.Equal(("synced", "Recorded", "joined", 15 * 60), (first.Status, first.Display, first.Outcome, first.AttendedSeconds));
        Assert.Equal(w.Tasks[0], first.OccurrenceTaskId);
        Assert.NotNull(first.OrganizerName);
    }

    [Fact]
    public async Task DirectAndSkipLevelManagers_SeeTheSummary()
    {
        var w = await SeedAsync();

        foreach (var caller in new[] { w.Manager, w.SkipLevelManager })
        {
            var result = await ReadAsync(w, caller, w.Subject);

            Assert.True(result.IsSuccess, result.Error);
            var rows = result.Value!.Items;
            Assert.Equal(3, rows.Count);
            Assert.Equal("Weekly sync", rows.Single(r => r.StartAt == Week(0)).Title);
        }
    }

    [Fact]
    public async Task FormerManager_Gets404()
    {
        // Review Focus 3: the reporting manager changed, so the old manager must not see the attendance any more.
        var w = await SeedAsync();

        var result = await ReadAsync(w, w.FormerManager, w.Subject);

        Assert.Equal((404, MeetingErrorCodes.NotFound), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task HrOrOwnerOutsideTheLine_Gets404()
    {
        var w = await SeedAsync();

        var result = await ReadAsync(w, w.Outsider, w.Subject);

        Assert.Equal((404, MeetingErrorCodes.NotFound), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task PrivateMeeting_IsMaskedForManagers_NotForSelf()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var details = await db.MeetingDetails.SingleAsync(d => d.TaskId == w.Tasks[0]);
            details.IsPrivate = true;
            await db.SaveChangesAsync();
        }

        var manager = await ReadAsync(w, w.Manager, w.Subject);
        var self = await ReadAsync(w, w.Subject, w.Subject);

        Assert.True(manager.IsSuccess, manager.Error);
        var masked = manager.Value!.Items.Single(r => r.StartAt == Week(0));
        Assert.Equal("Private meeting", masked.Title);
        Assert.Null(masked.OrganizerName);

        Assert.True(self.IsSuccess, self.Error);
        var own = self.Value!.Items.Single(r => r.StartAt == Week(0));
        Assert.Equal("Weekly sync", own.Title);
        Assert.NotNull(own.OrganizerName);
    }

    [Fact]
    public async Task UnfinishedOrUnsyncedOccurrences_AreUnknown_NeverAbsent()
    {
        var w = await SeedAsync();

        // Before the last occurrence has ended, only the two that ended are listed.
        var result = await ReadAsync(w, w.Subject, w.Subject, now: Week(2));

        Assert.True(result.IsSuccess, result.Error);
        var rows = result.Value!.Items;
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.StartAt == Week(2));

        // The occurrence that was never synced is unknown - never "absent".
        var unsynced = rows.Single(r => r.StartAt == Week(1));
        Assert.Equal(("pending", "Unknown", "unknown", (int?)null),
            (unsynced.Status, unsynced.Display, unsynced.Outcome, unsynced.AttendedSeconds));
        Assert.All(rows, r => Assert.DoesNotContain("absent", r.Outcome, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PerOccurrenceRemove_IsNotListed()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            db.MeetingOccurrenceAttendees.Add(new MeetingOccurrenceAttendee
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, OccurrenceTaskId = w.Tasks[0],
                AttendeeKind = MeetingAttendeeKinds.Employee, EmployeeId = w.Subject, Change = MeetingAttendeeChanges.Remove,
            });
            await db.SaveChangesAsync();
        }

        var result = await ReadAsync(w, w.Subject, w.Subject);

        Assert.True(result.IsSuccess, result.Error);
        var rows = result.Value!.Items;
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.StartAt == Week(0));
    }

    [Fact]
    public async Task WindowOver92Days_Is400()
    {
        var w = await SeedAsync();

        var result = await ReadAsync(w, w.Subject, w.Subject, from: Now.AddDays(-93), to: Now);

        Assert.Equal((400, MeetingErrorCodes.InvalidTime), (result.StatusCode, result.ErrorCode));
    }
}
