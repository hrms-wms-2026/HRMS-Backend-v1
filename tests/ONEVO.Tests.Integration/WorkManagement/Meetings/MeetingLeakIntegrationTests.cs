using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// Spec 5.1 leak coverage / D11: a meeting task never surfaces through the ordinary task reads - module and project
/// lists, my tasks, task counts and progress metrics, lookups by id - even when it is assigned to the reader.
/// <para>
/// Plan D Task 6: the four Plan D surfaces are leak-checked the same way. An outsider - not the organizer, not an
/// attendee, not a manager, not HR, not the project lead - sees nothing and gets the one 404 body
/// (<c>meeting_not_found</c>): the occurrence's action-item list, the reporting-line attendance view, the bulk
/// preview for an existing series, and the monitoring compliance view of another employee. A private meeting's real
/// title never appears in any response field.
/// </para>
/// </summary>
public sealed class MeetingLeakIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>14 days after the occurrence: it has ended, so the attendance view can list it.</summary>
    private static readonly DateTimeOffset Now = First.AddDays(14);

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Standard, Guid Meeting);

    /// <summary>
    /// The Plan D world: a meeting series with a materialized occurrence, an attendee on it, an outsider who is in
    /// the same tenant but on no meeting, and a private second occurrence of the same series.
    /// </summary>
    private sealed class PlanDWorld
    {
        public MyTeamDb H = null!;
        public Guid Organizer;
        public Guid OrganizerUserId;
        public Guid Attendee;
        public Guid AttendeeUserId;
        public Guid Outsider;
        public Guid OutsiderUserId;
        public Guid ProjectId;
        public Guid RootModuleId;
        public Guid StatusId;
        public Guid SeriesId;
        public Guid OccurrenceTaskId;
        public Guid PrivateOccurrenceTaskId;
        public string PrivateTitle = "Board Strategy";
    }

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var standard = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            assigneeEmployeeId: at.OrganizerEmployeeId);
        var (meeting, _) = h.AddOccurrence(db, h.AddSeries(db, at), at);
        meeting.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        db.TaskAssignments.Add(new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = meeting.Id, EmployeeId = at.OrganizerEmployeeId, UserId = Guid.NewGuid(),
            AssignedById = at.OrganizerEmployeeId, AssignedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return new World(h, at, standard.Id, meeting.Id);
    }

    /// <summary>
    /// The Plan D world. The occurrence is private and carries an active provider link, so the monitoring compliance
    /// view measures it - and masks its title. The outsider is in the same tenant and in the same project, so
    /// nothing but the meetings' own access rules keeps them out.
    /// </summary>
    private static async Task<PlanDWorld> SeedPlanDAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizerId, organizerUserId, attendeeId, attendeeUserId, outsiderId, outsiderUserId;
        Guid projectId, rootId, statusId, seriesId, occurrenceTaskId, privateTaskId;
        string privateTitle = "Board Strategy";

        await using (var db = h.NewContext())
        {
            var organizer = h.AddEmployee(db);
            var attendee = h.AddEmployee(db);
            var outsider = h.AddEmployee(db);
            (organizerId, organizerUserId) = (organizer.Id, organizer.UserId);
            (attendeeId, attendeeUserId) = (attendee.Id, attendee.UserId);
            (outsiderId, outsiderUserId) = (outsider.Id, outsider.UserId);
            (projectId, rootId) = h.AddProject(db, organizer.Id);
            statusId = h.AddStatus(db, projectId, marksComplete: false);

            // The outsider is a member of the project's root module: the ordinary task rules let them see the
            // module's tasks, so only the meeting's own access rules can hide the meeting.
            h.AddMember(db, projectId, rootId, outsider.Id);
            // The attendee is a member too, so the action item is visible to them under the ordinary task rules.
            h.AddMember(db, projectId, rootId, attendee.Id);

            var series = h.AddSeries(db, new MeetingPlacement(organizer.Id, projectId, rootId, statusId), First);
            series.Title = privateTitle;
            h.AddSeriesParticipant(db, series, attendee.Id, CalendarEventParticipantStatuses.Accepted);

            var (occurrence, details) = h.AddOccurrence(db, series, new MeetingPlacement(organizer.Id, projectId, rootId, statusId), First);
            occurrenceTaskId = occurrence.Id;
            occurrence.Title = privateTitle;
            details.IsPrivate = true;
            details.AttendanceStatus = MeetingAttendanceStatuses.Pending;

            var (privateOccurrence, privateDetails) = h.AddOccurrence(db, series,
                new MeetingPlacement(organizer.Id, projectId, rootId, statusId), First.AddDays(7));
            privateTaskId = privateOccurrence.Id;
            privateOccurrence.Title = privateTitle;
            privateDetails.IsPrivate = true;
            privateDetails.AttendanceStatus = MeetingAttendanceStatuses.Pending;

            // A provider link, so the compliance view measures the occurrence and knows its provider.
            var link = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, CalendarEventId = null,
                OwnerKind = CalendarMeetingOwnerKinds.MeetingSeries,
                ExternalCalendarConnectionId = Guid.NewGuid(),
                Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = "leak-link",
                JoinUrl = "https://teams.example/join/leak", Status = CalendarEventMeetingStatuses.Active,
                LinkGeneration = 1, CreatedAt = First.AddDays(-1),
            };
            db.CalendarEventMeetings.Add(link);
            db.MeetingSeriesLinks.Add(new MeetingSeriesLink
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, SeriesId = series.Id,
                CalendarEventMeetingId = link.Id, Origin = MeetingSeriesLinkOrigins.Created, AttachedAt = First.AddDays(-1),
            });
            details.CalendarEventMeetingId = link.Id;
            privateDetails.CalendarEventMeetingId = link.Id;
            seriesId = series.Id;

            await db.SaveChangesAsync();
        }

        return new PlanDWorld
        {
            H = h, Organizer = organizerId, OrganizerUserId = organizerUserId,
            Attendee = attendeeId, AttendeeUserId = attendeeUserId,
            Outsider = outsiderId, OutsiderUserId = outsiderUserId,
            ProjectId = projectId, RootModuleId = rootId, StatusId = statusId, SeriesId = seriesId,
            OccurrenceTaskId = occurrenceTaskId, PrivateOccurrenceTaskId = privateTaskId, PrivateTitle = privateTitle,
        };
    }

    [Fact]
    public async Task ModuleAndProjectLists_NeverContainTheMeeting()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var tasks = new EfWorkTaskRepository(db);

        Assert.Equal([w.Standard], (await tasks.GetByObjectiveIdAsync(w.H.TenantId, w.At.ObjectiveId)).Select(t => t.Id));
        Assert.Equal([w.Standard], (await tasks.GetByProjectAsync(w.H.TenantId, w.At.ProjectId)).Select(t => t.Id));
    }

    [Fact]
    public async Task MyTasks_NeverContainTheMeeting_EvenWhenItIsAssignedToMe()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var mine = await new EfWorkTaskRepository(db).GetMyActiveTasksAsync(w.H.TenantId, w.At.OrganizerEmployeeId,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        Assert.Contains(mine, r => r.Id == w.Standard);
        Assert.DoesNotContain(mine, r => r.Id == w.Meeting);
    }

    [Fact]
    public async Task CountsAndProgressMetrics_ExcludeTheMeeting()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var tasks = new EfWorkTaskRepository(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(1, (await tasks.CountByObjectivesAsync(w.H.TenantId, [w.At.ObjectiveId], today))[w.At.ObjectiveId].Total);
        Assert.Equal(1, (await tasks.CountByProjectsAsync(w.H.TenantId, [w.At.ProjectId], today))[w.At.ProjectId].Total);
        Assert.Single(await tasks.ListTopLevelProgressRowsAsync(w.H.TenantId, [w.At.ObjectiveId]));
        Assert.Single(await tasks.ListTopLevelOverdueAsync(w.H.TenantId, [w.At.ObjectiveId], today, take: 10));
    }

    [Fact]
    public async Task ATaskLookupById_IsNotFound_ForAMeeting()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var tasks = new EfWorkTaskRepository(db);

        Assert.Null(await tasks.GetByIdForTenantAsync(w.H.TenantId, w.Meeting));
        Assert.Null(await tasks.GetTrackedByIdForTenantAsync(w.H.TenantId, w.Meeting));
    }

    [Fact]
    public async Task ActionItemList_ForAnOutsider_Is404()
    {
        var w = await SeedPlanDAsync();
        await using (var db = w.H.NewContext())
        {
            // A real action item, so the list is not empty for the people who may see it.
            var task = w.H.AddTask(db, w.ProjectId, w.RootModuleId, w.StatusId, progress: 0, due: null);
            task.Title = "Ship the roadmap";
            db.MeetingActionItems.Add(new MeetingActionItem
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, OccurrenceTaskId = w.OccurrenceTaskId,
                TaskId = task.Id, CreatedByEmployeeId = w.Organizer, CreatedAt = First,
            });
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var service = ActionItems(ctx, w);

        // The positive control: the attendee may see the occurrence, so the list answers.
        var attendee = await service.ListAsync(w.H.TenantId, w.Attendee, w.AttendeeUserId, w.OccurrenceTaskId, CancellationToken.None);
        Assert.True(attendee.IsSuccess, attendee.Error);
        Assert.Single(attendee.Value!);

        // The outsider may not: the one 404 body, and nothing of the meeting in it.
        var outsider = await service.ListAsync(w.H.TenantId, w.Outsider, w.OutsiderUserId, w.OccurrenceTaskId, CancellationToken.None);

        Assert.False(outsider.IsSuccess);
        Assert.Equal((404, MeetingErrorCodes.NotFound), (outsider.StatusCode, outsider.ErrorCode));
        Assert.Null(outsider.Value);
        var body = JsonSerializer.Serialize(outsider);
        Assert.DoesNotContain(w.PrivateTitle, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ship the roadmap", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttendanceView_ForAnOutsider_Is404()
    {
        var w = await SeedPlanDAsync();
        await using var db = w.H.NewContext();
        var service = Attendance(db, w);

        // The positive control: the employee themself sees their own row.
        var self = await service.GetAsync(w.H.TenantId, w.Attendee, w.Attendee, null, null, CancellationToken.None);
        Assert.True(self.IsSuccess, self.Error);
        Assert.NotEmpty(self.Value!.Items);

        // The outsider is not in the reporting line: the one 404 body, and nothing of the meeting in it.
        var outsider = await service.GetAsync(w.H.TenantId, w.Outsider, w.Attendee, null, null, CancellationToken.None);

        Assert.False(outsider.IsSuccess);
        Assert.Equal((404, MeetingErrorCodes.NotFound), (outsider.StatusCode, outsider.ErrorCode));
        Assert.Null(outsider.Value);
        var body = JsonSerializer.Serialize(outsider);
        Assert.DoesNotContain(w.PrivateTitle, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.OccurrenceTaskId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BulkPreview_ForAnOutsiderWithASeriesId_Is404()
    {
        var w = await SeedPlanDAsync();
        await using var db = w.H.NewContext();
        var service = BulkPreview(db, w);

        // The positive control: the organizer may preview their own series.
        var organizer = await service.PreviewAsync(w.H.TenantId, w.Organizer, Request(w), CancellationToken.None);
        Assert.True(organizer.IsSuccess, organizer.Error);

        // The outsider may not: the one 404 body - not a 403, and nothing of the series in it.
        var outsider = await service.PreviewAsync(w.H.TenantId, w.Outsider, Request(w), CancellationToken.None);

        Assert.False(outsider.IsSuccess);
        Assert.Equal((404, MeetingErrorCodes.NotFound), (outsider.StatusCode, outsider.ErrorCode));
        Assert.Null(outsider.Value);
        var body = JsonSerializer.Serialize(outsider);
        Assert.DoesNotContain(w.PrivateTitle, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.SeriesId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Attendee.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MonitoringCompliance_ForAnotherEmployee_LeaksNoPrivateTitle()
    {
        var w = await SeedPlanDAsync();

        // Some usage inside the first occurrence, so the compliance view has a measured row.
        await using (var db = w.H.NewContext())
        {
            for (var i = 0; i < 15; i++)
                db.AppUsageSnapshots.Add(new AppUsageSnapshot
                {
                    Id = Guid.NewGuid(), TenantId = w.H.TenantId, EmployeeId = w.Attendee, AgentDeviceId = Guid.NewGuid(),
                    CapturedAt = First.AddMinutes(i), ProcessName = "teams.exe", CreatedAt = First,
                });
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var compliance = new EfMeetingComplianceReadRepository(ctx, new EfMeetingRepository(ctx));

        // The employee themself: the private meeting is listed, but never named.
        var own = await compliance.ListAsync(w.H.TenantId, w.Attendee, First.AddMinutes(-1), First.AddDays(8), CancellationToken.None);
        Assert.Equal(2, own.Count);
        Assert.Contains(own, i => i.EventId == w.OccurrenceTaskId);
        Assert.Contains(own, i => i.EventId == w.PrivateOccurrenceTaskId);
        Assert.All(own, i => Assert.Equal("Private meeting", i.Title));
        Assert.All(own, i => Assert.True(i.IsPrivate));
        Assert.DoesNotContain(w.PrivateTitle, JsonSerializer.Serialize(own), StringComparison.OrdinalIgnoreCase);

        // Another employee: nothing of that meeting at all.
        var other = await compliance.ListAsync(w.H.TenantId, w.Outsider, First.AddMinutes(-1), First.AddDays(8), CancellationToken.None);

        Assert.Empty(other);
        var body = JsonSerializer.Serialize(other);
        Assert.DoesNotContain(w.PrivateTitle, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.OccurrenceTaskId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.SeriesId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the services the four leak cases read through ----

    private static MeetingActionItemService ActionItems(ApplicationDbContext db, PlanDWorld w)
    {
        var meetings = new EfMeetingRepository(db);
        var membership = Membership(db);
        var hierarchy = new WorkHierarchyService(new EfObjectiveRepository(db), membership);
        var identity = new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new EfEntityAssetRepository(db));
        var taskAccess = new TaskAccessResolver(identity, new EfWorkTaskRepository(db), new EfProjectRepository(db),
            new EfProjectMemberRepository(db), new PermissionStub(), hierarchy);
        return new MeetingActionItemService(
            Options.Create(new MeetingsOptions { Create = true }), meetings, new MeetingAccessResolver(meetings),
            new EfMeetingActionItemRepository(db), new EfWorkTaskRepository(db), new EfTaskStatusRepository(db),
            taskAccess, new UnitOfWork(db), new SystemDateTimeProvider());
    }

    private static EmployeeMeetingAttendanceService Attendance(ApplicationDbContext db, PlanDWorld w)
        => new(new EfMeetingRepository(db), new EfMeetingAttendanceRepository(db),
            new CommonEfEmployeeRepository(db), new MeetingTestClock(Now));

    private static MeetingBulkInviteService BulkPreview(ApplicationDbContext db, PlanDWorld w)
        => new(Options.Create(new MeetingsOptions { Create = true, BulkInvite = true }),
            new CommonEfEmployeeRepository(db),
            new EfDepartmentRepository(db),
            new EfMeetingRepository(db),
            new PlacementResolver(db, w),
            new MeetingAccessResolver(new EfMeetingRepository(db)));

    /// <summary>The request the outsider sends: the existing series, and a department that does not exist - the
    /// series gate runs first, so nothing of the request is expanded.</summary>
    private static MeetingBulkPreviewRequest Request(PlanDWorld w)
        => new(w.ProjectId, w.RootModuleId, [], [], [], w.SeriesId, null);

    /// <summary>The real create gate over the seeded tree: the organizer owns the root module, the outsider does not.</summary>
    private sealed class PlacementResolver : IMeetingPlacementResolver
    {
        private readonly MeetingPlacementResolver _real;

        public PlacementResolver(ApplicationDbContext db, PlanDWorld w)
        {
            var objectives = new EfObjectiveRepository(db);
            var membership = new ONEVO.Application.Features.WorkManagement.Objectives.Services.MilestoneMembershipCoordinator(
                new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), objectives,
                new EfProjectMemberInvitationRepository(db),
                new ONEVO.Application.Features.WorkManagement.Common.Services.CallerIdentityResolver(
                    new CommonEfEmployeeRepository(db), new EfEntityAssetRepository(db)),
                new ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter(
                    db, new ONEVO.Infrastructure.Security.NoOpEncryptionService(), new SystemDateTimeProvider()));

            _real = new MeetingPlacementResolver(
                new ONEVO.Application.Features.WorkManagement.Hierarchy.WorkHierarchyService(objectives, membership),
                membership, new EfProjectRepository(db), objectives,
                new StubCurrentUser(w.H.TenantId), new StubOfficeReconciler());
        }

        public Task<Result<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement>> ResolveAsync(
            Guid tenantId, Guid callerEmployeeId, Guid? projectId, Guid? objectiveId, CancellationToken ct)
            => _real.ResolveAsync(tenantId, callerEmployeeId, projectId, objectiveId, ct);
    }

    private sealed class StubCurrentUser(Guid tenantId) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId => tenantId;
        public string Email => "organizer@leak-test.onevo.dev";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private sealed class StubOfficeReconciler : IOfficeProjectReconciler
    {
        public Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(OfficeProjectReconcileOutcome.AlreadyAligned);

        public Task<OfficeProjectReconcileOutcome?> TryReconcileAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<OfficeProjectReconcileOutcome?>(OfficeProjectReconcileOutcome.AlreadyAligned);
    }

    private static MilestoneMembershipCoordinator Membership(ApplicationDbContext db)
        => new(new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
            new EfProjectMemberInvitationRepository(db),
            new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new EfEntityAssetRepository(db)),
            new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()));

    private sealed class PermissionStub : IPermissionResolver
    {
        public Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, Guid? activeLegalEntityId, CancellationToken ct = default)
            => Task.FromResult(new List<string>());
    }
}
