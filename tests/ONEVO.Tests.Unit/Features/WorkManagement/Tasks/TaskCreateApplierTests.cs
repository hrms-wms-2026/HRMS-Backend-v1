using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

/// <summary>Planner create popover (2026-11-03): an approved request that carries its requester as assignee and a slot
/// is assigned to the requester and planned there; a failed plan never fails the approval.</summary>
public class TaskCreateApplierTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private static readonly Guid RequesterUserId = Guid.NewGuid();
    private static readonly Guid DeciderId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 10, 11, 0, 0, TimeSpan.Zero);

    private sealed record Kit(TaskCreateApplier Applier, Mock<ITaskAssignmentRepository> Assignments,
        Mock<IMilestoneMembershipCoordinator> Membership, Mock<IPlannerBlockWriter> Planner, List<string> Calls);

    private static Kit Build(bool requesterActive = true, Result? planResult = null, bool requesterInModule = true)
    {
        var calls = new List<string>();
        var writes = new Mock<ITaskWriteService>();
        writes.Setup(x => x.CreateAsync(TenantId, RequesterUserId, RequesterId, It.IsAny<TaskCreateInput>(), ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkTask>.Success(new WorkTask
            {
                Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Title = "Fix login bug",
                CreatedAt = DateTimeOffset.UtcNow,
            }));
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, RequesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requesterActive ? new Employee { Id = RequesterId, UserId = RequesterUserId, TenantId = TenantId } : null);
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, RequesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requesterInModule);
        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assign")).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);
        var planner = new Mock<IPlannerBlockWriter>();
        planner.Setup(x => x.PlanForAsync(TenantId, RequesterId, TaskId, Start, End, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("plan")).ReturnsAsync(planResult ?? Result.Success());

        var applier = new TaskCreateApplier(writes.Object, objectives.Object, membership.Object, null,
            assignments.Object, unitOfWork.Object, planner.Object);
        return new Kit(applier, assignments, membership, planner, calls);
    }

    private static string Payload(Guid? assignee, DateTimeOffset? start, DateTimeOffset? end)
        => JsonSerializer.Serialize(new TaskCreateInput(ObjectiveId, "Fix login bug", null, Guid.NewGuid(), "medium",
            null, 2m, null, null, null, assignee, start, end));

    private static ApprovalApplyContext Context(string requested, string? applied = null) => new(
        new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, RequestedByEmployeeId = RequesterId,
            ApproverEmployeeId = DeciderId, PayloadJson = requested, ActionType = WorkActionTypes.TaskCreate,
            CreatedAt = DateTimeOffset.UtcNow,
        },
        applied ?? requested, DeciderId);

    [Fact]
    public async Task Apply_WithAssigneeAndSlot_AssignsTheRequester_ThenPlans()
    {
        var kit = Build();

        var outcome = await kit.Applier.ApplyAsync(Context(Payload(RequesterId, Start, End)), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        kit.Assignments.Verify(x => x.AddAsync(It.Is<TaskAssignment>(a =>
            a.TaskId == TaskId && a.EmployeeId == RequesterId && a.UserId == RequesterUserId && a.AssignedById == DeciderId),
            It.IsAny<CancellationToken>()), Times.Once);
        kit.Membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ObjectiveId, RequesterId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["assign", "save", "plan"], kit.Calls);
    }

    [Fact]
    public async Task Apply_PlanFails_StillApplied_AndAssigned()
    {
        var kit = Build(planResult: Result.Failure("Past days can't be changed.", 400, "planner_day_read_only"));

        var outcome = await kit.Applier.ApplyAsync(Context(Payload(RequesterId, Start, End)), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        kit.Assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_WithoutNewFields_NoAssignmentAndNoPlan()
    {
        var kit = Build();

        var outcome = await kit.Applier.ApplyAsync(Context(Payload(null, null, null)), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        kit.Assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        kit.Planner.Verify(x => x.PlanForAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_ApproverChangedAssigneeOrSlot_IsInvalid()
    {
        var kit = Build();

        var outcome = await kit.Applier.ApplyAsync(
            Context(Payload(RequesterId, Start, End), Payload(RequesterId, Start.AddHours(1), End.AddHours(1))), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Invalid, outcome.Kind);
        Assert.Equal("The assignee and planned time cannot be changed by the approver.", outcome.Error);
        kit.Assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Review finding 3: a requester removed from the module between filing and approval is never re-added by the assign.
    [Fact]
    public async Task Apply_RequesterRemovedFromTheModule_CreatesTheTask_ButNeitherAssignsNorPlans()
    {
        var kit = Build(requesterInModule: false);

        var outcome = await kit.Applier.ApplyAsync(Context(Payload(RequesterId, Start, End)), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Applied, outcome.Kind);
        kit.Assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        kit.Membership.Verify(x => x.UpsertMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        kit.Planner.Verify(x => x.PlanForAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_RequesterNoLongerActive_IsStale()
    {
        var kit = Build(requesterActive: false);

        var outcome = await kit.Applier.ApplyAsync(Context(Payload(RequesterId, Start, End)), CancellationToken.None);

        Assert.Equal(ApplyOutcomeKind.Stale, outcome.Kind);
        kit.Assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
