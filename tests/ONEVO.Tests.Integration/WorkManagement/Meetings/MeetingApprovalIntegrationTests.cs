using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Appliers;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.2 / D4: a member's meeting.create request creates nothing until approved; approval creates it with
/// the requester as organizer, or goes Stale; applying twice creates one series.</summary>
public sealed class MeetingApprovalIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Requester, WorkApprovalRequest Request);

    private static async Task<World> SeedAsync(DateTimeOffset start)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var requester = h.AddEmployee(db);
        h.AddMember(db, at.ProjectId, at.ObjectiveId, requester.Id);
        var form = new CreateMeetingRequest(at.ProjectId, at.ObjectiveId, "Design review", null, null, false, start, 45, false,
            "UTC", null, [at.OrganizerEmployeeId], [], null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, ProjectId = at.ProjectId, ActionType = WorkActionTypes.MeetingCreate,
            TargetType = WorkTargetTypes.Meeting, TargetTitle = form.Title, ApproverSource = WorkApprovalSources.Hierarchy,
            ApproverEmployeeId = at.OrganizerEmployeeId, RequestedByEmployeeId = requester.Id,
            PayloadJson = JsonSerializer.Serialize(new MeetingCreatePayload(form, new MeetingApprovalSnapshot(
                form.Title, null, start, 45, "UTC", "Does not repeat", "Requester", "Project / Root", ["Lead"], [], null)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = WorkApprovalRequestStatuses.Pending, CreatedAt = Now,
        };
        db.WorkApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        return new World(h, at, requester.Id, request);
    }

    private static async Task<ApplyOutcome> ApplyAsync(World w, bool stillMember = true)
    {
        await using var db = w.H.NewContext();
        var membership = new MembershipStub(w.At.ObjectiveId, w.Requester, stillMember);
        var clock = new FixedClock(Now);
        var factory = new MeetingSeriesFactory(new EfMeetingRepository(db), new EfMeetingOccurrenceWriter(db),
            new MeetingTaskDefaults(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db)), new EfProjectRepository(db),
            new EfEmployeeRepository(db), new IcalNetMeetingRecurrence(), new UnitOfWork(db), clock,
            new MeetingTestServices(db, clock).Materializer);
        var applier = new MeetingCreateApplier(Microsoft.Extensions.Options.Options.Create(new ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions { Create = true }), factory, new EfMeetingRepository(db), new EfEmployeeRepository(db),
            new EfObjectiveRepository(db), membership, clock);
        var request = await db.WorkApprovalRequests.SingleAsync(r => r.Id == w.Request.Id);

        var outcome = await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            var result = await applier.ApplyAsync(new ApprovalApplyContext(request, request.PayloadJson, w.At.OrganizerEmployeeId), ct);
            await db.SaveChangesAsync(ct);
            return result;
        });
        return outcome;
    }

    [Fact]
    public async Task Approve_CreatesTheMeeting_WithTheRequesterAsOrganizer()
    {
        var w = await SeedAsync(Now.AddDays(2));

        var outcome = await ApplyAsync(w);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        await using var check = w.H.NewContext();
        var series = await check.MeetingSeries.SingleAsync();
        Assert.Equal((w.Requester, MeetingCreationPaths.Approval, w.Request.Id),
            (series.OrganizerEmployeeId, series.CreationPath, series.ApprovalRequestId));
        Assert.Equal(series.Id, (await check.WorkApprovalRequests.SingleAsync(r => r.Id == w.Request.Id)).TargetId);
        Assert.Equal(1, await check.MeetingDetails.CountAsync(d => d.SeriesId == series.Id));
    }

    [Fact]
    public async Task ApproveAfterTheStartPassed_IsStale_AndCreatesNothing()
    {
        var w = await SeedAsync(Now.AddMinutes(-5));

        Assert.Equal(ApplyOutcomeKind.Stale, (await ApplyAsync(w)).Kind);
        await using var check = w.H.NewContext();
        Assert.Equal(0, await check.MeetingSeries.CountAsync());
    }

    [Fact]
    public async Task ApproveAfterTheRequesterLeftTheModule_IsStale()
    {
        var w = await SeedAsync(Now.AddDays(2));

        Assert.Equal(ApplyOutcomeKind.Stale, (await ApplyAsync(w, stillMember: false)).Kind);
        await using var check = w.H.NewContext();
        Assert.Equal(0, await check.MeetingSeries.CountAsync());
    }

    [Fact]
    public async Task ApplyingTwice_CreatesOneSeries()
    {
        var w = await SeedAsync(Now.AddDays(2));

        Assert.Equal(ApplyOutcomeKind.Applied, (await ApplyAsync(w)).Kind);
        Assert.Equal(ApplyOutcomeKind.Applied, (await ApplyAsync(w)).Kind);

        await using var check = w.H.NewContext();
        Assert.Equal(1, await check.MeetingSeries.CountAsync());
        Assert.Equal(1, await check.MeetingDetails.CountAsync());
    }

    /// <summary>Answers only "is this employee still a member of this module"; nothing else may be called.</summary>
    private sealed class MembershipStub(Guid moduleId, Guid employeeId, bool isMember) : IMilestoneMembershipCoordinator
    {
        public Task<bool> IsEffectiveManagerAsync(Guid tenantId, Guid objectiveId, Guid employee, CancellationToken ct = default)
            => Task.FromResult(objectiveId == moduleId && employee == employeeId && isMember);

        public Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetActiveAssigneeAsync(Guid tenantId, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeactivateMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasOtherActiveAccessAsync(Guid tenantId, Guid projectId, Guid employee, Guid excludingObjectiveId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid objectiveId, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsEffectiveOwnerAsync(Guid tenantId, Guid objectiveId, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ONEVO.Application.Common.Models.Result<ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities.ProjectMemberInvitation>> ApplyMemberAddAsync(Guid tenantId, ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective module, Guid requestedByEmployeeId, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ONEVO.Application.Common.Models.Result> ApplyMemberRemoveAsync(Guid tenantId, ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective module, Guid employee, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }
}
