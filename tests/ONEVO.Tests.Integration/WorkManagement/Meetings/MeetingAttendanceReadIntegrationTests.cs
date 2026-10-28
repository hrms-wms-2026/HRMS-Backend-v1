using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 8 (spec 5.8, 5.9): an occurrence's attendance. The organizer sees every effective attendee (guests too)
/// and the link's unmatched sessions; an attendee sees only their own row; nobody is ever "absent" - before the occurrence is
/// synced a person without rows is unknown, after it "no join recorded".</summary>
public sealed class MeetingAttendanceReadIntegrationTests
{
    private const string Guest = "client@example.com";

    private static async Task<ONEVO.Application.Common.Models.Result<MeetingAttendanceResponse>> ReadAsync(World w, Guid caller,
        DateTimeOffset at, int k = 0)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at));
        return await s.AttendanceReads.GetAsync(w.H.TenantId, caller, w.SeriesId, Week(k), CancellationToken.None);
    }

    /// <summary>The attendee joined twice (time is the union), the guest joined, and the link has one session no occurrence fits.</summary>
    private static async Task<World> HeldAsync(bool settle)
    {
        var w = await SeedAsync(Guest);
        var stub = new SessionsStub
        {
            Answer = (_, _) =>
            [
                Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 10), Rec("a", w.AttendeeEmail, Week(0).AddMinutes(5), 10),
                    Rec("g", Guest, Week(0).AddMinutes(2), 20)),
            ],
        };
        foreach (var after in settle ? new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) } : [TimeSpan.FromMinutes(30)])
            await SyncAsync(w, stub, End(0) + after);
        await using var db = w.H.NewContext();
        db.MeetingAttendanceSessions.Add(new MeetingAttendanceSession
        {
            Id = Guid.NewGuid(), TenantId = w.H.TenantId, CalendarEventMeetingId = w.LinkId, ProviderSessionId = "ad-hoc",
            SessionStart = Week(0).AddDays(3), SessionEnd = Week(0).AddDays(3).AddMinutes(12),
        });
        await db.SaveChangesAsync();
        return w;
    }

    [Fact]
    public async Task Organizer_SeesEveryone_IncludingGuests_AndUnmatchedSessions()
    {
        var w = await HeldAsync(settle: true);

        var result = await ReadAsync(w, w.Organizer, End(0).AddDays(2));

        Assert.True(result.IsSuccess, result.Error);
        var view = result.Value!;
        Assert.Equal((MeetingAttendanceStatuses.Synced, "Recorded"), (view.Status, view.Display));
        var attendee = Assert.Single(view.Rows, r => r.EmployeeId == w.Attendee);
        Assert.Equal(("employee", 15 * 60, "joined"), (attendee.Kind, attendee.AttendedSeconds, attendee.Outcome));
        var guest = Assert.Single(view.Rows, r => r.Email == Guest);
        Assert.Equal(("guest", Guest, 20 * 60, "joined"), (guest.Kind, guest.DisplayName, guest.AttendedSeconds, guest.Outcome));
        var unmatched = Assert.Single(view.Unmatched!);
        Assert.Equal(Week(0).AddDays(3), unmatched.Start);
    }

    [Fact]
    public async Task Attendee_SeesOnlyOwnRow()
    {
        var w = await HeldAsync(settle: true);

        var result = await ReadAsync(w, w.Attendee, End(0).AddDays(2));

        var row = Assert.Single(result.Value!.Rows);
        Assert.Equal((w.Attendee, "joined"), (row.EmployeeId, row.Outcome));
        Assert.Null(result.Value.Unmatched);
    }

    [Fact]
    public async Task BeforeSynced_EveryoneWithoutRowsIsUnknown()
    {
        var w = await SeedAsync(Guest);
        await SyncAsync(w, new SessionsStub(), End(0).AddMinutes(30));   // first checkpoint: no session yet

        var result = await ReadAsync(w, w.Organizer, End(0).AddHours(1));

        Assert.Equal((MeetingAttendanceStatuses.Pending, "Unknown"), (result.Value!.Status, result.Value.Display));
        Assert.All(result.Value.Rows, r => Assert.Equal(("unknown", (int?)null), (r.Outcome, r.AttendedSeconds)));
        Assert.Equal(3, result.Value.Rows.Count);   // organizer, attendee, guest
    }

    [Fact]
    public async Task Synced_WithoutRowsIsNoJoinRecorded_NeverAbsent()
    {
        var w = await SeedAsync(Guest);
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("g", Guest, Week(0), 20))] };
        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) })
            await SyncAsync(w, stub, End(0) + after);

        var result = await ReadAsync(w, w.Organizer, End(0).AddDays(2));

        Assert.Equal(MeetingAttendanceStatuses.Synced, result.Value!.Status);
        Assert.Equal("no_join_recorded", Assert.Single(result.Value.Rows, r => r.EmployeeId == w.Attendee).Outcome);
        Assert.Equal("joined", Assert.Single(result.Value.Rows, r => r.Email == Guest).Outcome);
        Assert.DoesNotContain(result.Value.Rows, r => r.Outcome.Contains("absent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Provisional_IsRecorded_AndPeopleWithoutRowsStayUnknown()
    {
        var w = await SeedAsync(Guest);
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("g", Guest, Week(0), 20))] };
        await SyncAsync(w, stub, End(0).AddMinutes(30));

        var result = await ReadAsync(w, w.Organizer, End(0).AddHours(1));

        Assert.Equal((MeetingAttendanceStatuses.Provisional, "Recorded"), (result.Value!.Status, result.Value.Display));
        Assert.Equal("unknown", Assert.Single(result.Value.Rows, r => r.EmployeeId == w.Attendee).Outcome);
    }

    [Fact]
    public async Task Outsider404()
    {
        var w = await SeedAsync(Guest);
        Guid outsider;
        await using (var db = w.H.NewContext())
        {
            outsider = w.H.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        var result = await ReadAsync(w, outsider, End(0).AddDays(1));

        Assert.Equal((404, MeetingErrorCodes.NotFound), (result.StatusCode, result.ErrorCode));
    }
}
