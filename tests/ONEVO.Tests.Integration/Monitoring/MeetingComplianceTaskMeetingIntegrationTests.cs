using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.Monitoring;

/// <summary>
/// Plan D Task 5 (spec D17): the monitoring compliance view covers the task meetings as well as the legacy calendar
/// events, and a private meeting of either kind shows as "Private meeting" - its title hidden, its usage numbers
/// unchanged.
/// </summary>
public sealed class MeetingComplianceTaskMeetingIntegrationTests
{
    private static readonly DateTimeOffset From = Week(0).AddMinutes(-1);
    private static readonly DateTimeOffset To = Week(2).AddMinutes(1);

    /// <summary>15 teams samples inside the first occurrence: 15 * 60 s = 900 s of a 1800 s meeting (50%).</summary>
    private const int TeamsSamples = 15;

    private static EfMeetingComplianceReadRepository Compliance(ApplicationDbContext db)
        => new(db, new EfMeetingRepository(db));

    /// <summary>
    /// The first occurrence was materialized before the series had a link, so it carries none. Every occurrence of a
    /// series uses the series' link (spec 4.2), so attach it here - the compliance view only measures an occurrence
    /// with an active provider link.
    /// </summary>
    private static async Task AttachLinkAsync(World w)
    {
        await using var db = w.H.NewContext();
        var details = await db.MeetingDetails.SingleAsync(d => d.TaskId == w.Tasks[0]);
        details.CalendarEventMeetingId = w.LinkId;
        details.AttendanceStatus = MeetingAttendanceStatuses.Pending;
        await db.SaveChangesAsync();
    }

    /// <summary>Usage samples inside the first occurrence: the provider's app, and one app of another provider.</summary>
    private static async Task SeedUsageAsync(World w, string providerProcess = "teams.exe", string otherProcess = "zoom.exe")
    {
        await using var db = w.H.NewContext();
        for (var i = 0; i < TeamsSamples; i++)
            db.AppUsageSnapshots.Add(new AppUsageSnapshot
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, EmployeeId = w.Attendee, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = Week(0).AddMinutes(i), ProcessName = providerProcess, CreatedAt = Week(0),
            });
        // The other provider's app inside the same window: it must not count for a Teams meeting.
        for (var i = 0; i < 3; i++)
            db.AppUsageSnapshots.Add(new AppUsageSnapshot
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, EmployeeId = w.Attendee, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = Week(0).AddMinutes(20 + i), ProcessName = otherProcess, CreatedAt = Week(0),
            });
        await db.SaveChangesAsync();
    }

    /// <summary>A legacy calendar event with an active Teams link, and the employee as its participant.</summary>
    private static async Task<Guid> SeedLegacyEventAsync(World w, string title, bool isPrivate)
    {
        await using var db = w.H.NewContext();
        var organizer = await db.Employees.SingleAsync(e => e.Id == w.Organizer);
        var connection = await db.ExternalCalendarConnections.SingleAsync();
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = w.H.TenantId, Title = title, IsPrivate = isPrivate, CreatedById = organizer.UserId,
            StartDate = Week(0), EndDate = End(0),
        };
        db.PersonalCalendarEvents.Add(calendarEvent);
        db.CalendarEventParticipants.Add(new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = w.H.TenantId, EventId = calendarEvent.Id, EmployeeId = w.Attendee,
            ResponseStatus = CalendarEventParticipantStatuses.Accepted,
        });
        db.CalendarEventMeetings.Add(new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = w.H.TenantId, CalendarEventId = calendarEvent.Id,
            OwnerKind = CalendarMeetingOwnerKinds.CalendarEvent,
            ExternalCalendarConnectionId = connection.Id,
            Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = $"legacy-{calendarEvent.Id}",
            Status = CalendarEventMeetingStatuses.Active, LinkGeneration = 1, CreatedAt = Week(0),
        });
        await db.SaveChangesAsync();
        return calendarEvent.Id;
    }

    [Fact]
    public async Task Compliance_IncludesTaskMeetingOccurrences_WithTheirProviderApp()
    {
        var w = await SeedAsync();
        await AttachLinkAsync(w);
        await SeedUsageAsync(w);

        await using var db = w.H.NewContext();
        var items = await Compliance(db).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);

        // Every materialized occurrence of the weekly meeting is a row, keyed by its occurrence task id.
        Assert.Equal(w.Tasks.Order(), items.Select(i => i.EventId).Order());
        var first = Assert.Single(items, i => i.EventId == w.Tasks[0]);
        Assert.Equal((Week(0), End(0)), (first.StartsAt, first.EndsAt));
        Assert.Equal("Weekly sync", first.Title);
        Assert.False(first.IsPrivate);
        // Only the provider's own app counts: 15 samples * 60 s of a 1800 s meeting = 50%. The zoom samples do not.
        Assert.Equal(900, first.MeetingApplicationSeconds);
        Assert.Equal(900, first.RequiredSeconds);
        Assert.Equal(50m, first.UsagePercentage);
        Assert.True(first.IsCompliant);
    }

    [Fact]
    public async Task Compliance_PrivateMeetingsAreMasked_BothKinds()
    {
        var w = await SeedAsync();
        await AttachLinkAsync(w);
        await SeedUsageAsync(w);
        await SeedLegacyEventAsync(w, "Legacy sync", isPrivate: true);
        await SeedLegacyEventAsync(w, "Public sync", isPrivate: false);
        // The first occurrence is private; the second is not.
        await using (var db = w.H.NewContext())
        {
            var details = await db.MeetingDetails.SingleAsync(d => d.TaskId == w.Tasks[0]);
            details.IsPrivate = true;
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var items = await Compliance(ctx).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);

        // The private task meeting and the private legacy event are listed, never named.
        var privateTaskMeeting = Assert.Single(items, i => i.EventId == w.Tasks[0]);
        Assert.Equal("Private meeting", privateTaskMeeting.Title);
        Assert.True(privateTaskMeeting.IsPrivate);

        var privateLegacy = Assert.Single(items, i => i.Title == "Private meeting" && i.EventId != w.Tasks[0]);
        Assert.True(privateLegacy.IsPrivate);

        // The public ones keep their real title.
        Assert.Equal("Public sync", Assert.Single(items, i => i.Title == "Public sync").Title);
        Assert.Equal("Weekly sync", Assert.Single(items, i => i.EventId == w.Tasks[1]).Title);

        // The usage numbers are unchanged by the masking: the private legacy event measures exactly like the public one.
        var publicLegacy = Assert.Single(items, i => i.Title == "Public sync");
        Assert.Equal(publicLegacy.MeetingApplicationSeconds, privateLegacy.MeetingApplicationSeconds);
        Assert.Equal(publicLegacy.UsagePercentage, privateLegacy.UsagePercentage);
        Assert.Equal(900, privateTaskMeeting.MeetingApplicationSeconds);
    }

    [Fact]
    public async Task Compliance_DeclinedOrRemovedAttendee_HasNoRow()
    {
        var w = await SeedAsync();
        await AttachLinkAsync(w);
        await SeedUsageAsync(w);

        // The positive control: as a plain accepted attendee the employee has a row.
        await using (var accepted = w.H.NewContext())
        {
            var items = await Compliance(accepted).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);
            Assert.NotEmpty(items);
        }

        // The employee declines the series.
        await using (var db = w.H.NewContext())
        {
            var participant = await db.CalendarEventParticipants
                .SingleAsync(p => p.MeetingSeriesId == w.SeriesId && p.EmployeeId == w.Attendee);
            participant.ResponseStatus = CalendarEventParticipantStatuses.Rejected;
            await db.SaveChangesAsync();
        }

        await using (var declined = w.H.NewContext())
        {
            var items = await Compliance(declined).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);
            Assert.Empty(items);
        }

        // The employee is removed from the series instead.
        await using (var db = w.H.NewContext())
        {
            var participant = await db.CalendarEventParticipants
                .SingleAsync(p => p.MeetingSeriesId == w.SeriesId && p.EmployeeId == w.Attendee);
            participant.ResponseStatus = CalendarEventParticipantStatuses.Accepted;
            participant.IsDeleted = true;
            participant.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var removed = w.H.NewContext())
        {
            var items = await Compliance(removed).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);
            Assert.Empty(items);
        }
    }

    [Fact]
    public async Task Compliance_AnotherTenantsOccurrence_IsNeverListed()
    {
        var w = await SeedAsync();
        await AttachLinkAsync(w);

        // The same employee id in another tenant, with its own occurrence and link: it must never leak into the
        // first tenant's compliance view.
        var other = await w.H.AddTenantAsync();
        await using (var db = w.H.NewContext())
        {
            var at = other.AddPlacement(db);
            var series = other.AddSeries(db, at);
            var (_, details) = other.AddOccurrence(db, series, at);
            other.AddSeriesParticipant(db, series, w.Attendee, CalendarEventParticipantStatuses.Accepted);
            var link = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = other.TenantId, CalendarEventId = null,
                OwnerKind = CalendarMeetingOwnerKinds.MeetingSeries,
                ExternalCalendarConnectionId = Guid.NewGuid(),
                Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = "other-link",
                JoinUrl = "https://teams.example/join/other", Status = CalendarEventMeetingStatuses.Active,
                LinkGeneration = 1, CreatedAt = Week(0),
            };
            db.CalendarEventMeetings.Add(link);
            db.MeetingSeriesLinks.Add(new MeetingSeriesLink
            {
                Id = Guid.NewGuid(), TenantId = other.TenantId, SeriesId = series.Id,
                CalendarEventMeetingId = link.Id, Origin = MeetingSeriesLinkOrigins.Created, AttachedAt = Week(0),
            });
            details.CalendarEventMeetingId = link.Id;
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var items = await Compliance(ctx).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);

        Assert.DoesNotContain(items, i => i.Title == "Private meeting" && i.EventId != w.Tasks[0]);
        Assert.All(items, i => Assert.Contains(i.EventId, w.Tasks));
    }

    [Fact]
    public async Task C2_AgentRatio_ReachesTheTaskMeetingView()
    {
        var w = await SeedAsync();
        await AttachLinkAsync(w);
        await SeedUsageAsync(w);

        await using var db = w.H.NewContext();
        var items = await Compliance(db).ListAsync(w.H.TenantId, w.Attendee, From, To, CancellationToken.None);

        // The engine builds its agent dictionary from the compliance rows, keyed by (event id, start).
        var agent = items.GroupBy(c => (c.EventId, c.StartsAt)).ToDictionary(g => g.Key, g => g.First().UsagePercentage / 100m);

        await using var ctx = w.H.NewContext();
        var employee = await ctx.Employees.AsNoTracking().SingleAsync(e => e.Id == w.Attendee);
        var source = new TaskMeetingOccurrenceSource(new EfMeetingRepository(ctx), new EfCalendarEventRepository(ctx),
            new EfMeetingAttendanceRepository(ctx));
        var views = await source.ListAsync(new EmployeeOccurrenceContext(w.H.TenantId, employee, From, To,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { employee.Email! }, ReportingManagerId: null, agent, Week(0)),
            CancellationToken.None);

        var view = Assert.Single(views, v => v.EventId == w.Tasks[0]);
        Assert.Equal(0.5m, view.AgentUsageRatio);
    }
}
