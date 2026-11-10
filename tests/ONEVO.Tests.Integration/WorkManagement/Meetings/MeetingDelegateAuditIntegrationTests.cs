using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.Calendar;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec "Audit log": a delegate's action on the principal's meeting writes one audit_logs row in the same
/// transaction, with the delegate as the user and the principal in the JSON; the organizer's own actions write none.</summary>
public sealed class MeetingDelegateAuditIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Now.AddDays(2);

    private sealed record World(MyTeamDb H, MeetingPlacement At, MeetingSeries Series, Employee Secretary, Employee Invitee);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var series = h.AddSeries(db, at, Start);
        h.AddOccurrence(db, series, at);
        var secretary = h.AddEmployee(db);
        var invitee = h.AddEmployee(db);
        db.MeetingDelegates.Add(new MeetingDelegate
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, PrincipalEmployeeId = at.OrganizerEmployeeId, DelegateEmployeeId = secretary.Id,
            CreatedByUserId = Guid.NewGuid(), CreatedAt = Now.AddDays(-1),
        });
        await db.SaveChangesAsync();
        return new World(h, at, series, secretary, invitee);
    }

    private static MeetingTestServices Services(MyTeamDb h, ApplicationDbContext db, Guid actingUserId)
    {
        var clock = new MeetingTestClock(Now);
        var audit = new MeetingDelegateAudit(new EfAuditLogRepository(db), new CalendarNotificationDb.StubCurrentUser(h.TenantId, actingUserId), clock);
        return new MeetingTestServices(db, clock, audit, new EfMeetingDelegateRepository(db));
    }

    private static async Task<List<AuditLog>> AuditRowsAsync(World w)
    {
        await using var db = w.H.NewContext();
        return await db.AuditLogs.AsNoTracking().Where(a => a.TenantId == w.H.TenantId && a.ResourceType == "MeetingSeries").ToListAsync();
    }

    [Fact]
    public async Task ADelegatedCancel_RecordsOneAudit_WithTheDelegateAndThePrincipal()
    {
        var w = await SeedAsync();

        await using (var db = w.H.NewContext())
        {
            var result = await Services(w.H, db, w.Secretary.UserId).Cancels.CancelAsync(w.H.TenantId, w.Secretary.Id, w.Secretary.UserId,
                w.Series.Id, Start, MeetingScopes.All, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        var row = Assert.Single(await AuditRowsAsync(w));
        Assert.Equal((MeetingAuditActions.Cancelled, (Guid?)w.Secretary.UserId, (Guid?)w.Series.Id), (row.Action, row.UserId, row.ResourceId));
        Assert.Contains(w.At.OrganizerEmployeeId.ToString(), row.NewValuesJson);
    }

    [Fact]
    public async Task ADelegatedAttendeeAdd_RecordsOneAudit()
    {
        var w = await SeedAsync();

        await using (var db = w.H.NewContext())
        {
            var result = await Services(w.H, db, w.Secretary.UserId).Attendees.AddSeriesAttendeesAsync(w.H.TenantId, w.Secretary.Id, w.Series.Id,
                new MeetingAttendeesRequest([w.Invitee.Id], []), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        var row = Assert.Single(await AuditRowsAsync(w));
        Assert.Equal(MeetingAuditActions.AttendeesChanged, row.Action);
        Assert.Contains(w.Invitee.Id.ToString(), row.NewValuesJson);
    }

    [Fact]
    public async Task ADelegatedEdit_RecordsTheChangedFields_WithTheOldValues()
    {
        var w = await SeedAsync();
        var oldTitle = w.Series.Title;

        await using (var db = w.H.NewContext())
        {
            var series = await db.MeetingSeries.AsNoTracking().SingleAsync(s => s.Id == w.Series.Id);
            var result = await Services(w.H, db, w.Secretary.UserId).Edits.EditAsync(w.H.TenantId, w.Secretary.Id, w.Secretary.UserId,
                w.Series.Id, Start, new EditMeetingRequest(MeetingScopes.All, series.Revision, "Board review", null, null, null, null, null, null,
                    null, null), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        var row = Assert.Single(await AuditRowsAsync(w));
        Assert.Equal(MeetingAuditActions.Edited, row.Action);
        Assert.Contains("Board review", row.NewValuesJson);
        Assert.Contains(oldTitle, row.OldValuesJson);
    }

    [Fact]
    public async Task TheOrganizersOwnCancel_RecordsNoMeetingAudit()
    {
        var w = await SeedAsync();
        var organizerUser = await OrganizerUserAsync(w);

        await using (var db = w.H.NewContext())
        {
            var result = await Services(w.H, db, organizerUser).Cancels.CancelAsync(w.H.TenantId, w.At.OrganizerEmployeeId, organizerUser,
                w.Series.Id, Start, MeetingScopes.All, CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        Assert.Empty(await AuditRowsAsync(w));
    }

    private static async Task<Guid> OrganizerUserAsync(World w)
    {
        await using var db = w.H.NewContext();
        return (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == w.At.OrganizerEmployeeId)).UserId;
    }
}
