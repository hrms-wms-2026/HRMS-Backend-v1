using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// Spec 5.4 / D14: an action item is an ordinary task in a module the creator is a member of, linked to the
/// occurrence it was raised from. The link is written with the task (direct create) or when the approval is
/// applied (via approval), and only the link's creator or the organizer may unlink it. The task itself
/// survives an unlink.
/// </summary>
public sealed class MeetingActionItemIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The world every test starts from: a project with a root module (the meeting's module), two
    /// child modules (M1 - the attendee may read it, M2 - they may not), a meeting series with one
    /// materialized occurrence, the organizer and an attendee.</summary>
    private sealed class World
    {
        public MyTeamDb H = null!;
        public Guid Organizer;
        public Guid OrganizerUserId;
        public Guid Attendee;
        public Guid AttendeeUserId;
        public Guid OtherAttendee;
        public Guid OtherAttendeeUserId;
        public Guid ProjectId;
        public Guid RootModuleId;
        public Guid ReadableModuleId;
        public Guid HiddenModuleId;
        public Guid CategoryId;
        public Guid StatusId;
        public Guid OccurrenceTaskId;
        public MeetingSeries Series = null!;
    }

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();

        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        var otherAttendee = h.AddEmployee(db);
        var (projectId, rootId) = h.AddProject(db, organizer.Id);
        var statusId = h.AddStatus(db, projectId, marksComplete: false);
        var readable = h.AddModule(db, projectId, rootId, organizer.Id);
        var hidden = h.AddModule(db, projectId, rootId, organizer.Id);
        // The attendee is a member of the readable module only: the hidden module is a sibling, so the
        // ordinary task rules do not let them see its tasks.
        h.AddMember(db, projectId, readable, attendee.Id);

        var series = h.AddSeries(db, new MeetingPlacement(organizer.Id, projectId, rootId, statusId), First);
        h.AddSeriesParticipant(db, series, attendee.Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, otherAttendee.Id, CalendarEventParticipantStatuses.Accepted);
        var (occurrence, _) = h.AddOccurrence(db, series, new MeetingPlacement(organizer.Id, projectId, rootId, statusId), First);
        await db.SaveChangesAsync();

        return new World
        {
            H = h, Organizer = organizer.Id, OrganizerUserId = organizer.UserId,
            Attendee = attendee.Id, AttendeeUserId = attendee.UserId,
            OtherAttendee = otherAttendee.Id, OtherAttendeeUserId = otherAttendee.UserId,
            ProjectId = projectId, RootModuleId = rootId, ReadableModuleId = readable, HiddenModuleId = hidden,
            CategoryId = h.CategoryIdFor(projectId), StatusId = statusId, OccurrenceTaskId = occurrence.Id, Series = series,
        };
    }

    [Fact]
    public async Task DirectCreate_LinksTheTaskToTheOccurrence_InOneTransaction()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.RootModuleId, w.OccurrenceTaskId);

        Assert.True(result.IsSuccess, result.Error);
        var taskId = result.Value!.Task!.Id;
        await using var check = w.H.NewContext();
        var link = await check.MeetingActionItems.SingleAsync();
        Assert.Equal((taskId, w.OccurrenceTaskId, w.Organizer), (link.TaskId, link.OccurrenceTaskId, link.CreatedByEmployeeId));
    }

    [Fact]
    public async Task AnotherModuleTheCreatorIsMemberOf_IsAllowed_OthersAreRejected()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var allowed = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.ReadableModuleId, w.OccurrenceTaskId);
        Assert.True(allowed.IsSuccess, allowed.Error);

        // A module the creator is not a member of: the existing task-create rule rejects it (403), and
        // nothing is written - not the task, not the link. It is a module of another project, whose root
        // the creator does not own, so the rule really rejects it.
        Guid otherModuleId;
        Guid otherCategoryId;
        await using (var other = w.H.NewContext())
        {
            var stranger = w.H.AddEmployee(other);
            var (otherProjectId, otherRootId) = w.H.AddProject(other, stranger.Id);
            otherModuleId = w.H.AddModule(other, otherProjectId, otherRootId, stranger.Id);
            otherCategoryId = w.H.CategoryIdFor(otherProjectId);
            await other.SaveChangesAsync();
        }
        var rejected = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, otherModuleId, w.OccurrenceTaskId, categoryId: otherCategoryId);

        Assert.False(rejected.IsSuccess);
        Assert.Equal(403, rejected.StatusCode);
        await using var check = w.H.NewContext();
        Assert.Equal(1, await check.MeetingActionItems.CountAsync());
        Assert.Equal(1, await check.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
    }

    [Fact]
    public async Task ViaApproval_LinkIsWrittenOnApply_NotOnRequest_RejectWritesNone()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.RootModuleId, w.OccurrenceTaskId, pendingApproval: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Task);
        var requestId = result.Value.ApprovalRequestId!.Value;
        await using (var check = w.H.NewContext())
        {
            // The request itself writes no link and no task.
            Assert.Equal(0, await check.MeetingActionItems.CountAsync());
            Assert.Equal(0, await check.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
        }

        await using (var approve = w.H.NewContext())
        {
            var outcome = await ApplyAsync(w, approve, requestId);
            Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        }

        await using (var afterApply = w.H.NewContext())
        {
            Assert.Equal(1, await afterApply.MeetingActionItems.CountAsync());
            Assert.Equal(1, await afterApply.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
        }

        // A rejected request writes neither.
        var rejectedResult = await CreateTaskAsync(w, w.H.NewContext(), w.Organizer, w.OrganizerUserId, w.RootModuleId, w.OccurrenceTaskId, pendingApproval: true);
        var rejectedId = rejectedResult.Value!.ApprovalRequestId!.Value;
        await using (var reject = w.H.NewContext())
        {
            var request = await reject.WorkApprovalRequests.SingleAsync(r => r.Id == rejectedId);
            request.Status = WorkApprovalRequestStatuses.Rejected;
            request.DecidedAt = DateTimeOffset.UtcNow;
            await reject.SaveChangesAsync();
        }

        await using var check2 = w.H.NewContext();
        Assert.Equal(1, await check2.MeetingActionItems.CountAsync());
        Assert.Equal(1, await check2.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
    }

    [Fact]
    public async Task ApprovedRequest_ForARemovedOccurrence_CreatesTheTaskWithoutALink()
    {
        var w = await SeedAsync();
        // A second occurrence that is soft-deleted (a split surplus) - gone as far as the meeting is concerned.
        Guid removedTaskId;
        await using (var db = w.H.NewContext())
        {
            var (task, _) = w.H.AddOccurrence(db, w.Series, new MeetingPlacement(w.Organizer, w.ProjectId, w.RootModuleId, w.StatusId), First.AddDays(7));
            removedTaskId = task.Id;
            await db.SaveChangesAsync();
        }
        await using (var db = w.H.NewContext())
        {
            var task = await db.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).SingleAsync(t => t.Id == removedTaskId);
            task.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        await using (var db = w.H.NewContext())
        {
            var outcome = await ApplyAsync(w, db, await FileRequestAsync(w, db, removedTaskId));

            // The task is still created; the link is simply not written, and the apply does not fail.
            Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        }

        await using var check = w.H.NewContext();
        Assert.Equal(0, await check.MeetingActionItems.CountAsync());
        Assert.Equal(1, await check.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
    }

    [Fact]
    public async Task ApproverCannotRepointTheSourceMeeting_ApplyIsInvalid_AndWritesNothing()
    {
        // The requester's source was checked when they filed it; an approver editing it to another meeting would attach
        // the task to a meeting neither of them may see.
        var w = await SeedAsync();
        var unrelatedOccurrenceId = await SeedUnrelatedMeetingAsync(w);

        await using (var db = w.H.NewContext())
        {
            var requestId = await FileRequestAsync(w, db, w.OccurrenceTaskId);
            var outcome = await ApplyAsync(w, db, requestId, approverPayloadJson: PayloadFor(w, unrelatedOccurrenceId));

            Assert.Equal(ApplyOutcomeKind.Invalid, outcome.Kind);
        }

        await using var check = w.H.NewContext();
        Assert.Equal(0, await check.MeetingActionItems.CountAsync());
        Assert.Equal(0, await check.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
    }

    [Fact]
    public async Task ApprovedRequest_WhoseRequesterCannotSeeTheSource_CreatesTheTaskWithoutALink()
    {
        // The requester may have been removed from the meeting while the request waited; the link is re-checked for them
        // at apply time, like a removed occurrence (no link, the task is still created).
        var w = await SeedAsync();
        Guid outsiderId;
        await using (var db = w.H.NewContext())
        {
            var outsider = w.H.AddEmployee(db);
            w.H.AddMember(db, w.ProjectId, w.RootModuleId, outsider.Id);
            await db.SaveChangesAsync();
            outsiderId = outsider.Id;
        }

        await using (var db = w.H.NewContext())
        {
            var outcome = await ApplyAsync(w, db, await FileRequestAsync(w, db, w.OccurrenceTaskId, outsiderId));
            Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        }

        await using var check = w.H.NewContext();
        Assert.Equal(0, await check.MeetingActionItems.CountAsync());
        Assert.Equal(1, await check.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
    }

    [Fact]
    public async Task ApproverReformattingThePayload_KeepsTheSameSource_AndLinks()
    {
        // Only a different source is refused: an approver payload that differs in formatting or other fields still links.
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var requestId = await FileRequestAsync(w, db, w.OccurrenceTaskId);
            var edited = JsonSerializer.Serialize(new TaskCreateInput(w.RootModuleId, "Action item (renamed)", null, w.CategoryId,
                WorkTaskPriorities.High, null, null, null, null, w.OccurrenceTaskId), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(ApplyOutcomeKind.Applied, (await ApplyAsync(w, db, requestId, edited)).Kind);
        }

        await using var check = w.H.NewContext();
        Assert.Equal(1, await check.MeetingActionItems.CountAsync());
    }

    [Fact]
    public async Task ActionItems_HiddenTaskIsNotListedOrCounted()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var visible = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.ReadableModuleId, w.OccurrenceTaskId);
        var hidden = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.HiddenModuleId, w.OccurrenceTaskId);
        Assert.True(visible.IsSuccess, visible.Error);
        Assert.True(hidden.IsSuccess, hidden.Error);

        await using var check = w.H.NewContext();
        var service = ActionItems(check, w);

        var listed = await service.ListAsync(w.H.TenantId, w.Attendee, w.AttendeeUserId, w.OccurrenceTaskId, CancellationToken.None);

        Assert.True(listed.IsSuccess, listed.Error);
        var taskIds = listed.Value!.Select(v => v.TaskId).ToList();
        Assert.Contains(visible.Value!.Task!.Id, taskIds);
        Assert.DoesNotContain(hidden.Value!.Task!.Id, taskIds);
        Assert.Equal(1, taskIds.Count);   // no count leak
    }

    [Fact]
    public async Task ActionItems_AnUnassignedItemHasNoAssignee_AnAssignedOneNamesIt()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var unassigned = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.ReadableModuleId, w.OccurrenceTaskId);
        var assigned = await CreateTaskAsync(w, db, w.Organizer, w.OrganizerUserId, w.ReadableModuleId, w.OccurrenceTaskId);
        Assert.True(unassigned.IsSuccess, unassigned.Error);
        Assert.True(assigned.IsSuccess, assigned.Error);

        db.TaskAssignments.Add(new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = assigned.Value!.Task!.Id, EmployeeId = w.Attendee,
            UserId = w.AttendeeUserId, AssignedById = w.Organizer, AssignedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await using var check = w.H.NewContext();
        var listed = await ActionItems(check, w)
            .ListAsync(w.H.TenantId, w.Attendee, w.AttendeeUserId, w.OccurrenceTaskId, CancellationToken.None);

        Assert.True(listed.IsSuccess, listed.Error);
        var views = listed.Value!.ToDictionary(v => v.TaskId);
        // No assignment means no assignee - not an empty guid.
        Assert.Null(views[unassigned.Value!.Task!.Id].AssigneeEmployeeId);
        Assert.Equal(w.Attendee, views[assigned.Value!.Task!.Id].AssigneeEmployeeId);
    }

    [Fact]
    public async Task Unlink_ByCreatorOrOrganizer_AttendeeGets403_TaskStays()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        // The link's creator is an attendee (a member of the readable module), not the organizer: so the
        // creator and the organizer are two different people, and the attendee role is a third one.
        var byAttendeeCreated = await CreateTaskAsync(w, db, w.Attendee, w.AttendeeUserId, w.ReadableModuleId, w.OccurrenceTaskId);
        Assert.True(byAttendeeCreated.IsSuccess, byAttendeeCreated.Error);
        var creatorTaskId = byAttendeeCreated.Value!.Task!.Id;

        // A second action item of the same creator, for the creator's own unlink.
        var second = await CreateTaskAsync(w, db, w.Attendee, w.AttendeeUserId, w.ReadableModuleId, w.OccurrenceTaskId);
        Assert.True(second.IsSuccess, second.Error);
        var secondTaskId = second.Value!.Task!.Id;

        // An attendee who is neither the creator nor the organizer may not.
        await using (var check = w.H.NewContext())
        {
            var service = ActionItems(check, w);
            var byOther = await service.UnlinkAsync(w.H.TenantId, w.OtherAttendee, w.OccurrenceTaskId, creatorTaskId, CancellationToken.None);
            Assert.False(byOther.IsSuccess);
            Assert.Equal((403, MeetingErrorCodes.OrganizerRequired), (byOther.StatusCode, byOther.ErrorCode));
        }

        // The organizer may unlink, even though they did not create the link.
        await using (var check = w.H.NewContext())
        {
            var service = ActionItems(check, w);
            var byOrganizer = await service.UnlinkAsync(w.H.TenantId, w.Organizer, w.OccurrenceTaskId, creatorTaskId, CancellationToken.None);
            Assert.True(byOrganizer.IsSuccess, byOrganizer.Error);
        }

        // The link's creator may unlink their own link.
        await using (var check = w.H.NewContext())
        {
            var service = ActionItems(check, w);
            var byCreator = await service.UnlinkAsync(w.H.TenantId, w.Attendee, w.OccurrenceTaskId, secondTaskId, CancellationToken.None);
            Assert.True(byCreator.IsSuccess, byCreator.Error);
        }

        await using var final = w.H.NewContext();
        Assert.Equal(0, await final.MeetingActionItems.CountAsync());
        Assert.Equal(2, await final.WorkTasks.CountAsync(t => t.Id == creatorTaskId || t.Id == secondTaskId));   // the tasks themselves stay
    }

    [Fact]
    public async Task InvisibleOccurrenceAsSource_Is404_AndCreatesNothing()
    {
        var w = await SeedAsync();
        // An outsider: a member of the project (so the task-create rule passes) but not of the meeting.
        var outsider = default(ONEVO.Domain.Features.CoreHr.Entities.Employee)!;
        await using (var db = w.H.NewContext())
        {
            outsider = w.H.AddEmployee(db);
            w.H.AddMember(db, w.ProjectId, w.RootModuleId, outsider.Id);
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var result = await CreateTaskAsync(w, ctx, outsider.Id, outsider.UserId, w.RootModuleId, w.OccurrenceTaskId);

        Assert.False(result.IsSuccess);
        Assert.Equal((404, MeetingErrorCodes.NotFound), (result.StatusCode, result.ErrorCode));
        await using var check = w.H.NewContext();
        Assert.Equal(0, await check.MeetingActionItems.CountAsync());
        Assert.Equal(0, await check.WorkTasks.CountAsync(t => t.TaskKind != WorkTaskKinds.Meeting));
    }

    [Fact]
    public async Task ASplitWithAnActionItem_CancelsTheSurplusInsteadOfDeletingIt()
    {
        var h = await MyTeamDb.CreateAsync();
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            var organizer = h.AddEmployee(db);
            var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, "FREQ=WEEKLY;COUNT=3");
            var (week0, _) = h.AddOccurrence(db, series, at, First);
            var (week1, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
            var (week2, _) = h.AddOccurrence(db, series, at, First.AddDays(14));
            // A real action item on the surplus occurrence.
            var actionTask = h.AddTask(db, series.ProjectId, series.ObjectiveId, at.StatusId, progress: 0, due: null);
            db.MeetingActionItems.Add(new MeetingActionItem
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, OccurrenceTaskId = week2.Id, TaskId = actionTask.Id,
                CreatedByEmployeeId = organizer.Id, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var services = new MeetingTestServices(db, new MeetingTestClock(First.AddDays(6)));
            var edited = await services.Edits.EditAsync(
                h.TenantId, organizer.Id, Guid.NewGuid(), series.Id, First.AddDays(7),
                new EditMeetingRequest(MeetingScopes.Following, 0, null, null, null, null, null, null, null, null,
                    "FREQ=WEEKLY;COUNT=1"),
                CancellationToken.None);
            Assert.True(edited.IsSuccess, edited.Error);

            await using var check = h.NewContext();
            var details = await check.MeetingDetails.ToDictionaryAsync(d => d.TaskId);
            // The surplus occurrence is cancelled, not deleted: something hangs off it.
            Assert.NotNull(details[week2.Id].CancelledAt);
            var surplus = await check.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == week2.Id);
            Assert.False(surplus.IsDeleted);
            // The action item itself is untouched.
            Assert.Equal(1, await check.MeetingActionItems.CountAsync(a => a.OccurrenceTaskId == week2.Id));
            Assert.Equal(1, await check.WorkTasks.IgnoreQueryFilters().CountAsync(t => t.Id == actionTask.Id));
        }
    }

    // --- wiring -------------------------------------------------------------------------------

    private static MeetingActionItemService ActionItems(ApplicationDbContext db, World w)
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

    private static MilestoneMembershipCoordinator Membership(ApplicationDbContext db)
        => new(new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
            new EfProjectMemberInvitationRepository(db),
            new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new EfEntityAssetRepository(db)),
            new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()));

    private static TaskWriteService Writes(ApplicationDbContext db)
    {
        var objectives = new EfObjectiveRepository(db);
        var tasks = new EfWorkTaskRepository(db);
        return new TaskWriteService(objectives, new EfProjectRepository(db), tasks, new EfTaskStatusRepository(db),
            new EfSprintRepository(db), new EfTaskCategoryRepository(db),
            new ObjectiveAllocationSlackCalculator(objectives, tasks), new EfCalendarEventRepository(db),
            new EfSprintActivityLogRepository(db), new EfTaskEditLogRepository(db), new EfTaskPercentageLogRepository(db));
    }

    private static async Task<Result<TaskWriteOutcome>> CreateTaskAsync(
        World w, ApplicationDbContext db, Guid creatorEmployeeId, Guid creatorUserId, Guid moduleId, Guid? sourceOccurrenceId,
        bool pendingApproval = false, Guid? categoryId = null)
    {
        var membership = Membership(db);
        var hierarchy = new WorkHierarchyService(new EfObjectiveRepository(db), membership);
        var identity = new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new EfEntityAssetRepository(db));
        var approvals = new ApprovalEngineStub(db, pendingApproval);
        var handler = new CreateTaskCommandHandler(
            new CurrentUserStub(w.H.TenantId, creatorUserId), identity, new EfObjectiveRepository(db), new UnitOfWork(db),
            membership, new AssetLinkerStub(), Writes(db), hierarchy, approvals, new NotificationStub(),
            Options.Create(new MeetingsOptions { Create = true }), ActionItems(db, w));

        return await handler.Handle(new CreateTaskCommand(moduleId, "Action item", null, categoryId ?? w.CategoryId, WorkTaskPriorities.Medium,
            null, null, null, null, null, sourceOccurrenceId), CancellationToken.None);
    }

    /// <summary>Files a pending task.create approval request for a removed occurrence, and returns its id.</summary>
    private static async Task<Guid> FileRequestAsync(World w, ApplicationDbContext db, Guid sourceOccurrenceId, Guid? requesterEmployeeId = null)
    {
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = w.H.TenantId, ProjectId = w.ProjectId, ActionType = WorkActionTypes.TaskCreate,
            TargetType = WorkTargetTypes.Task, TargetTitle = "Action item", ApproverSource = WorkApprovalSources.Hierarchy,
            ApproverEmployeeId = w.Organizer, RequestedByEmployeeId = requesterEmployeeId ?? w.Organizer,
            PayloadJson = PayloadFor(w, sourceOccurrenceId),
            Status = WorkApprovalRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.WorkApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static string PayloadFor(World w, Guid sourceOccurrenceId)
        => JsonSerializer.Serialize(new TaskCreateInput(w.RootModuleId, "Action item", null, w.CategoryId, WorkTaskPriorities.Medium,
            null, null, null, null, sourceOccurrenceId), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<ApplyOutcome> ApplyAsync(World w, ApplicationDbContext db, Guid requestId, string? approverPayloadJson = null)
    {
        var applier = new TaskCreateApplier(Writes(db), new EfObjectiveRepository(db), Membership(db), ActionItems(db, w));
        var request = await db.WorkApprovalRequests.SingleAsync(r => r.Id == requestId);
        return await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            var outcome = await applier.ApplyAsync(
                new ApprovalApplyContext(request, approverPayloadJson ?? request.PayloadJson, w.Organizer), ct);
            await db.SaveChangesAsync(ct);
            return outcome;
        });
    }

    /// <summary>A private meeting in the same project that nobody in the world but its own organizer attends.</summary>
    private static async Task<Guid> SeedUnrelatedMeetingAsync(World w)
    {
        await using var db = w.H.NewContext();
        var stranger = w.H.AddEmployee(db);
        var at = new MeetingPlacement(stranger.Id, w.ProjectId, w.RootModuleId, w.StatusId);
        var series = w.H.AddSeries(db, at, First);
        var (occurrence, _) = w.H.AddOccurrence(db, series, at, First);
        await db.SaveChangesAsync();
        return occurrence.Id;
    }

    private sealed class CurrentUserStub(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => "test@example.com";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private sealed class PermissionStub : IPermissionResolver
    {
        public Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, Guid? activeLegalEntityId, CancellationToken ct = default)
            => Task.FromResult(new List<string>());
    }

    private sealed class AssetLinkerStub : ITaskAssetLinker
    {
        public Task SyncAttachmentsAsync(Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncDescriptionImagesAsync(Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncCommentAttachmentsAsync(Guid tenantId, Guid userId, Guid commentId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncCommentDescriptionImagesAsync(Guid tenantId, Guid userId, Guid commentId, string? descriptionHtml, CancellationToken ct = default) => Task.CompletedTask;
        public Task CopyAttachmentsAsync(Guid tenantId, Guid userId, Guid sourceTaskId, Guid destinationTaskId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncChecklistTemplateAttachmentsAsync(Guid tenantId, Guid userId, Guid templateId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task CopyChecklistTemplateFilesToTaskAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> templateFileIds, Guid? templateId, Guid destinationTaskId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NotificationStub : IWorkNotificationEngine
    {
        public Task NotifyAsync(WorkNotificationEvent notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task InboxChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, string kind, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Direct by default; with <paramref name="pending"/> it files a real wm_approval_requests row.</summary>
    private sealed class ApprovalEngineStub(ApplicationDbContext db, bool pending) : IWorkApprovalEngine
    {
        public Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default)
        {
            if (!pending)
                return Task.FromResult(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

            var request = new WorkApprovalRequest
            {
                Id = Guid.NewGuid(), TenantId = action.TenantId, ProjectId = action.ProjectId, ActionType = action.ActionType,
                TargetType = action.TargetType, TargetTitle = action.TargetTitle, ApproverSource = WorkApprovalSources.Hierarchy,
                ApproverEmployeeId = action.ActorEmployeeId, RequestedByEmployeeId = action.ActorEmployeeId,
                PayloadJson = action.PayloadJson, Status = WorkApprovalRequestStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.WorkApprovalRequests.Add(request);
            return Task.FromResult(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(request.Id, action.ActorEmployeeId)));
        }
    }
}
