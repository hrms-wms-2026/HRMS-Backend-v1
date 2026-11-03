using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using Placement = ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.5 / D20 moves: forward-only for a series (history keeps its module), a single occurrence keeps its id
/// and leaves a suppression; a member's move becomes a meeting.move request.</summary>
public sealed class MeetingMoveIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(6);

    private sealed record World(MyTeamDb H, MeetingSeries Series, MeetingPlacement At, Guid Destination, Guid Week0, Guid Week1, Guid Week2, Guid Organizer);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var destination = h.AddModule(db, at.ProjectId, at.ObjectiveId, at.OrganizerEmployeeId);
        var series = h.AddSeries(db, at, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
        var (w0, _) = h.AddOccurrence(db, series, at, First);
        var (w1, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        var (w2, _) = h.AddOccurrence(db, series, at, First.AddDays(14));
        await db.SaveChangesAsync();
        return new World(h, series, at, destination, w0.Id, w1.Id, w2.Id, at.OrganizerEmployeeId);
    }

    private sealed class PlacementStub(bool direct) : IMeetingPlacementResolver
    {
        public Task<Result<Placement>> ResolveAsync(Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct)
            => Task.FromResult(Result<Placement>.Success(new Placement(projectId!.Value, objectiveId!.Value, false, direct)));
    }

    private sealed class ApprovalsStub : IWorkApprovalEngine
    {
        public WorkAction? Submitted { get; private set; }
        public Guid RequestId { get; } = Guid.NewGuid();

        public Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default)
        {
            Submitted = action;
            return Task.FromResult(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(RequestId, Guid.NewGuid())));
        }
    }

    private static MeetingMoveService Moves(ApplicationDbContext db, bool direct = true, ApprovalsStub? approvals = null)
    {
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        return new MeetingMoveService(s.Options, s.Meetings, s.Writer, s.Materializer, s.Access, new PlacementStub(direct),
            approvals ?? new ApprovalsStub(), s.Splitter, s.Recurrence, s.UnitOfWork, s.Clock, s.Links, s.Snapshots);
    }

    [Fact]
    public async Task AForwardMove_TakesTheUpcomingOccurrences_AndHistoryKeepsItsModule()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var moved = await Moves(db).MoveAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First.AddDays(7),
                new MoveMeetingRequest(MeetingScopes.Following, w.At.ProjectId, w.Destination), CancellationToken.None);
            Assert.True(moved.IsSuccess, moved.Error);
            Assert.Equal("moved", moved.Value!.Outcome);
        }

        await using var check = w.H.NewContext();
        var tasks = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).ToDictionaryAsync(t => t.Id);
        Assert.Equal((w.At.ObjectiveId, w.Destination, w.Destination), (tasks[w.Week0].ObjectiveId, tasks[w.Week1].ObjectiveId, tasks[w.Week2].ObjectiveId));
        var logs = await check.TaskEditLogs.Where(l => l.Source == TaskEditLogSources.MeetingMove).Select(l => l.TaskId).ToListAsync();
        Assert.Equal(new[] { w.Week1, w.Week2 }.Order(), logs.Order());   // one objective row per moved task, none for history
    }

    [Fact]
    public async Task ASingleMove_KeepsTheTaskId_AndLeavesASuppression()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            Assert.True((await Moves(db).MoveAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First.AddDays(14),
                new MoveMeetingRequest(MeetingScopes.This, w.At.ProjectId, w.Destination), CancellationToken.None)).IsSuccess);

        await using var check = w.H.NewContext();
        var details = await check.MeetingDetails.SingleAsync(d => d.TaskId == w.Week2);
        Assert.NotEqual(w.Series.Id, details.SeriesId);
        Assert.Equal(w.Destination, (await check.MeetingSeries.SingleAsync(s => s.Id == details.SeriesId)).ObjectiveId);
        var suppression = await check.MeetingOccurrenceSuppressions.SingleAsync(s => s.SeriesId == w.Series.Id);
        Assert.Equal((First.AddDays(14), w.Week2), (suppression.OriginalStart, suppression.MovedToTaskId));
        var materialize = await new MeetingTestServices(check, new MeetingTestClock(Now)).Materializer.EnsureAsync(
            w.H.TenantId, await check.MeetingSeries.SingleAsync(s => s.Id == w.Series.Id), First.AddDays(14), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(404, materialize.StatusCode);   // the slot never comes back
    }

    [Fact]
    public async Task AMembersMove_BecomesARequest_AndMovesNothing()
    {
        var w = await SeedAsync();
        var approvals = new ApprovalsStub();
        await using (var db = w.H.NewContext())
        {
            var result = await Moves(db, direct: false, approvals).MoveAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id,
                First.AddDays(7), new MoveMeetingRequest(MeetingScopes.Following, w.At.ProjectId, w.Destination), CancellationToken.None);
            Assert.Equal(("requested", approvals.RequestId), (result.Value!.Outcome, result.Value.ApprovalRequestId));
        }

        Assert.Equal((WorkActionTypes.MeetingMove, w.Destination), (approvals.Submitted!.ActionType, approvals.Submitted.TargetModuleId));
        await using var check = w.H.NewContext();
        Assert.Equal(1, await check.MeetingSeries.CountAsync());
    }

    [Fact]
    public async Task TheSameModule_Is400_AndNothingUpcoming_Is409()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var same = await Moves(db).MoveAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First.AddDays(7),
            new MoveMeetingRequest(MeetingScopes.Following, w.At.ProjectId, w.At.ObjectiveId), CancellationToken.None);
        var past = await Moves(db).MoveAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First,
            new MoveMeetingRequest(MeetingScopes.This, w.At.ProjectId, w.Destination), CancellationToken.None);

        Assert.Equal((400, MeetingErrorCodes.SameModule), (same.StatusCode, same.ErrorCode));
        Assert.Equal(409, past.StatusCode);
    }
}
