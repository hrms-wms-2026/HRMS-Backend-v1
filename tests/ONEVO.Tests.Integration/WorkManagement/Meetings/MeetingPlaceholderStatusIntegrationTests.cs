using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Final-review I7: a status that only meeting tasks reference is still deletable - meetings carry a
/// placeholder status (theirs is derived, D10) and are re-pointed, never counted as "tasks in this status".</summary>
public sealed class MeetingPlaceholderStatusIntegrationTests
{
    private sealed class Caller(Guid tenantId) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId => tenantId;
        public string Email => "lead@example.test";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private sealed class Identity(Guid employeeId) : ICallerIdentityResolver
    {
        public Task<Guid?> ResolveCallerEmployeeIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default) => Task.FromResult<Guid?>(employeeId);
        public Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, ONEVO.Application.Features.WorkManagement.Common.Services.EmployeeIdentityDto>> ResolveIdentitiesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoSweep : ITaskStatusChangeRequestConflictSweeper
    {
        public Task<int> MarkConflictingStaleAsync(Guid tenantId, Guid projectId, Guid decidedByEmployeeId,
            ONEVO.Application.Features.WorkManagement.Tasks.DTOs.TaskStatusChangeFootprint footprint, Guid? excludingRequestId, CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task AStatusUsedOnlyByMeetings_CanBeDeleted_AndTheMeetingsMoveToAnotherPlaceholder()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        Guid meetingOnly, meetingTask;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);                                   // its status is the first Active one
            meetingOnly = h.AddStatus(db, at.ProjectId, marksComplete: false);
            h.AddStatus(db, at.ProjectId, marksComplete: true);
            (await db.TaskStatuses.FindAsync(meetingOnly))!.DisplayOrder = 7;
            var series = h.AddSeries(db, at);
            var (task, _) = h.AddOccurrence(db, series, at);
            task.StatusId = meetingOnly;
            meetingTask = task.Id;
            await db.SaveChangesAsync();
        }

        await using (var db = h.NewContext())
        {
            var handler = Handler(db, h.TenantId, at.OrganizerEmployeeId);
            var result = await handler.Handle(new DeleteTaskStatusCommand(meetingOnly), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        await using var check = h.NewContext();
        Assert.False(await check.TaskStatuses.AnyAsync(s => s.Id == meetingOnly));
        var task2 = await check.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).SingleAsync(t => t.Id == meetingTask);
        Assert.Equal(at.StatusId, task2.StatusId);
    }

    [Fact]
    public async Task AStatusAnOrdinaryTaskUses_IsStillProtected()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        Guid used;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            used = h.AddStatus(db, at.ProjectId, marksComplete: false);
            h.AddTask(db, at.ProjectId, at.ObjectiveId, used, progress: 0, due: null);
            await db.SaveChangesAsync();
        }

        await using var context = h.NewContext();
        var result = await Handler(context, h.TenantId, at.OrganizerEmployeeId).Handle(new DeleteTaskStatusCommand(used), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
    }

    private static DeleteTaskStatusCommandHandler Handler(ApplicationDbContext db, Guid tenantId, Guid employeeId)
        => new(new Caller(tenantId), new Identity(employeeId), new EfObjectiveRepository(db), new EfProjectRepository(db),
            new EfTaskStatusRepository(db), new EfWorkTaskRepository(db), new ONEVO.Infrastructure.Persistence.UnitOfWork(db), new NoSweep(),
            new EfMeetingRepository(db));
}
