using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.Calendar;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using Placement = ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Meetings-as-tasks D5 / spec 5 access tiers: the approver of a meeting request sees its snapshot, frozen at
/// submit; the requester and anyone outside the request do not, and nobody gets the raw payload.</summary>
public sealed class MeetingApprovalSnapshotIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(-1);

    private static async Task<Result<ApprovalDetailResponse>> DetailAsAsync(MyTeamDb h, Guid callerUserId, Guid requestId)
    {
        await using var db = h.NewContext();
        var handler = new GetApprovalDetailQueryHandler(
            new CalendarNotificationDb.StubCurrentUser(h.TenantId, callerUserId),
            new CallerIdentityResolver(new EfEmployeeRepository(db), new EfEntityAssetRepository(db)),
            new EfWorkApprovalRequestRepository(db),
            // The detail reads only the project's module tree, which needs no membership coordinator.
            new WorkHierarchyService(new EfObjectiveRepository(db), null!),
            new EfProjectMemberInvitationRepository(db), new EfWorkTaskRepository(db), new EfSprintRepository(db), new EfProjectRepository(db));
        return await handler.Handle(new GetApprovalDetailQuery(requestId, ApprovalFeedSources.Engine), CancellationToken.None);
    }

    private static async Task<Guid> UserOfAsync(MyTeamDb h, Guid employeeId)
    {
        await using var db = h.NewContext();
        return (await db.Employees.SingleAsync(e => e.Id == employeeId)).UserId;
    }

    [Fact]
    public async Task TheApprover_SeesTheSnapshot_TheRequesterAndAnOutsider_DoNot()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid approver, requester, outsider, requestId;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            approver = at.OrganizerEmployeeId;
            requester = h.AddEmployee(db).Id;
            h.AddMember(db, at.ProjectId, at.ObjectiveId, requester);
            outsider = h.AddEmployee(db).Id;
            var form = new CreateMeetingRequest(at.ProjectId, at.ObjectiveId, "Design review", null, null, false, First, 45, false,
                "UTC", null, [approver], ["client@example.com"], null);
            var snapshot = new MeetingApprovalSnapshot(form.Title, null, First, 45, "UTC", "Does not repeat", "Requester",
                "Project / Root", ["Lead"], ["client@example.com"], null);
            var request = new WorkApprovalRequest
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, ProjectId = at.ProjectId, PositionObjectiveId = at.ObjectiveId,
                ActionType = WorkActionTypes.MeetingCreate, TargetType = WorkTargetTypes.Meeting, TargetTitle = form.Title,
                ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = approver, RequestedByEmployeeId = requester,
                PayloadJson = JsonSerializer.Serialize(new MeetingCreatePayload(form, snapshot), Json),
                Status = WorkApprovalRequestStatuses.Pending, CreatedAt = Now,
            };
            db.WorkApprovalRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var asApprover = await DetailAsAsync(h, await UserOfAsync(h, approver), requestId);
        Assert.True(asApprover.IsSuccess, asApprover.Error);
        using (var extra = JsonDocument.Parse(asApprover.Value!.ExtraJson!))
        {
            var snapshot = extra.RootElement.GetProperty("snapshot");
            Assert.Equal("Design review", snapshot.GetProperty("title").GetString());
            Assert.Equal("Project / Root", snapshot.GetProperty("modulePath").GetString());
        }
        Assert.Null(asApprover.Value.RequestedPayloadJson);

        var asRequester = await DetailAsAsync(h, await UserOfAsync(h, requester), requestId);
        Assert.True(asRequester.IsSuccess, asRequester.Error);
        Assert.Null(asRequester.Value!.ExtraJson);
        Assert.Null(asRequester.Value.RequestedPayloadJson);

        var asOutsider = await DetailAsAsync(h, await UserOfAsync(h, outsider), requestId);
        Assert.Equal(404, asOutsider.StatusCode);
    }

    private sealed class PlacementNeedsApproval : IMeetingPlacementResolver
    {
        public Task<Result<Placement>> ResolveAsync(Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct)
            => Task.FromResult(Result<Placement>.Success(new Placement(projectId!.Value, objectiveId!.Value, false, Direct: false)));
    }

    private sealed class CapturingApprovals : IWorkApprovalEngine
    {
        public WorkAction? Submitted { get; private set; }

        public Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default)
        {
            Submitted = action;
            return Task.FromResult(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(Guid.NewGuid(), Guid.NewGuid())));
        }
    }

    [Fact]
    public async Task AMoveRequest_StoresTheSnapshot_FrozenAtSubmit()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizer, approver, destination, projectId, seriesId, attendee;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            organizer = at.OrganizerEmployeeId;
            projectId = at.ProjectId;
            approver = h.AddEmployee(db).Id;
            destination = h.AddModule(db, at.ProjectId, at.ObjectiveId, approver);
            var series = h.AddSeries(db, at, First, "FREQ=WEEKLY");
            series.Title = "Weekly sync";
            seriesId = series.Id;
            attendee = h.AddEmployee(db, lastName: "Arjun").Id;
            h.AddSeriesParticipant(db, series, organizer, CalendarEventParticipantStatuses.Accepted);
            h.AddSeriesParticipant(db, series, attendee, CalendarEventParticipantStatuses.Accepted);
            await db.SaveChangesAsync();
        }

        var approvals = new CapturingApprovals();
        await using (var db = h.NewContext())
        {
            var s = new MeetingTestServices(db, new MeetingTestClock(Now));
            var moves = new MeetingMoveService(s.Options, s.Meetings, s.Writer, s.Materializer, s.Access, new PlacementNeedsApproval(),
                approvals, s.Splitter, s.Recurrence, s.UnitOfWork, s.Clock, s.Links, s.Snapshots);
            var result = await moves.MoveAsync(h.TenantId, organizer, Guid.NewGuid(), seriesId, First,
                new MoveMeetingRequest(MeetingScopes.Following, projectId, destination), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal("requested", result.Value!.Outcome);
        }

        Guid requestId;
        await using (var db = h.NewContext())
        {
            var submitted = approvals.Submitted!;
            var request = new WorkApprovalRequest
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, ProjectId = projectId, PositionObjectiveId = destination,
                ActionType = submitted.ActionType, TargetType = submitted.TargetType, TargetId = seriesId, TargetTitle = "Weekly sync",
                ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = approver, RequestedByEmployeeId = organizer,
                PayloadJson = submitted.PayloadJson, Status = WorkApprovalRequestStatuses.Pending, CreatedAt = Now,
            };
            db.WorkApprovalRequests.Add(request);
            // After submit: the series is renamed and gains a participant. The approver must still see what was submitted.
            var series = await db.MeetingSeries.SingleAsync(x => x.Id == seriesId);
            series.Title = "Renamed later";
            h.AddSeriesParticipant(db, series, h.AddEmployee(db, lastName: "Late").Id, CalendarEventParticipantStatuses.Pending);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var detail = await DetailAsAsync(h, await UserOfAsync(h, approver), requestId);
        Assert.True(detail.IsSuccess, detail.Error);
        using var extra = JsonDocument.Parse(detail.Value!.ExtraJson!);
        var snapshot = extra.RootElement.GetProperty("snapshot");
        Assert.Equal("Weekly sync", snapshot.GetProperty("title").GetString());
        var names = snapshot.GetProperty("participantNames").EnumerateArray().Select(n => n.GetString()!).ToList();
        Assert.Single(names);
        Assert.EndsWith("Arjun", names[0]);
        Assert.Equal("FREQ=WEEKLY", snapshot.GetProperty("recurrenceSummary").GetString());
    }
}
