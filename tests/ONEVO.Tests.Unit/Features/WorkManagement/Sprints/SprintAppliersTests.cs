using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Appliers;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using TaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintAppliersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid RequesterUser = Guid.NewGuid();
    private static readonly Guid Position = Guid.NewGuid();

    private readonly SprintTestWiring _w = new(TenantId, ProjectId);
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private Sprint? _sprint;

    public SprintAppliersTests()
    {
        _w.Projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "P", IsActive = true });
        _w.Assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), Requester, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty));
        _w.Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, Requester, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Requester, TenantId = TenantId, UserId = RequesterUser });
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask>());
        _w.Sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _sprint?.Id == id ? _sprint : null);
    }

    private Sprint GivenSprint(string status)
    {
        _sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S1", Status = status,
            StartDate = status == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 1),
            EndDate = status == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 14),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        return _sprint;
    }

    private static WorkApprovalRequest Request(string actionType, Guid? targetId, string payload, DateTimeOffset? snapshot = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = actionType, TargetType = WorkTargetTypes.Sprint,
        TargetId = targetId, RequestedByEmployeeId = Requester, PositionObjectiveId = Position, PayloadJson = payload,
        TargetUpdatedAtSnapshot = snapshot
    };

    private static Task<ApplyOutcome> Apply(IApprovalActionApplier applier, WorkApprovalRequest request)
        => applier.ApplyAsync(new ApprovalApplyContext(request, request.PayloadJson, Guid.NewGuid()), CancellationToken.None);

    [Fact]
    public async Task Create_AppliesAsRequester_StoresCreatedSprintIdAsTarget()
    {
        var request = Request(WorkActionTypes.SprintCreate, null,
            "{\"projectId\":\"" + ProjectId + "\",\"name\":\"Sprint 9\",\"goal\":null,\"taskIds\":[]}");

        var outcome = await Apply(new SprintCreateApplier(_w.Writes(), _w.Membership.Object, _objectives.Object), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
        request.TargetId.Should().NotBeNull();
        _w.Sprints.Verify(x => x.AddAsync(It.Is<Sprint>(s =>
            s.Id == request.TargetId && s.Name == "Sprint 9" && s.CreatedById == RequesterUser && s.CreatorPositionObjectiveId == Position),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_SprintChangedAfterRequest_Stale()
    {
        var sprint = GivenSprint(SprintStatuses.Active);

        var outcome = await Apply(new SprintEditApplier(_w.Sprints.Object, _w.Writes()),
            Request(WorkActionTypes.SprintEdit, sprint.Id, "{\"name\":\"New\"}", snapshot: DateTimeOffset.UtcNow.AddDays(-5)));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Stale);
        sprint.Name.Should().Be("S1");
    }

    [Fact]
    public async Task Start_ValidationFails_Invalid()
    {
        var sprint = GivenSprint(SprintStatuses.Active);

        var outcome = await Apply(new SprintStartApplier(_w.Sprints.Object, _w.Writes()),
            Request(WorkActionTypes.SprintStart, sprint.Id, "{\"startDate\":\"2026-10-01\",\"endDate\":\"2026-10-14\",\"goal\":null}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Invalid);
        outcome.Error.Should().Be("Only a Draft sprint can be started.");
    }

    [Fact]
    public async Task Complete_AppliesDisposition()
    {
        var sprint = GivenSprint(SprintStatuses.Active);
        var todo = Guid.NewGuid();
        var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id, StatusId = todo };
        _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
        _w.Statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, todo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = todo, MarksTaskComplete = false });

        var outcome = await Apply(new SprintCompleteApplier(_w.Sprints.Object, _w.Writes()),
            Request(WorkActionTypes.SprintComplete, sprint.Id, "{\"disposition\":\"backlog\",\"targetSprintId\":null}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
        sprint.Status.Should().Be(SprintStatuses.Complete);
        task.SprintId.Should().BeNull();
    }

    [Fact]
    public async Task Delete_ActiveNow_Invalid()
    {
        var sprint = GivenSprint(SprintStatuses.Active);

        var outcome = await Apply(new SprintDeleteApplier(_w.Sprints.Object, _w.Writes()),
            Request(WorkActionTypes.SprintDelete, sprint.Id, "{}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Invalid);
        _w.Sprints.Verify(x => x.Remove(It.IsAny<Sprint>()), Times.Never);
    }

    [Fact]
    public async Task Delete_SprintGone_Stale()
    {
        var outcome = await Apply(new SprintDeleteApplier(_w.Sprints.Object, _w.Writes()),
            Request(WorkActionTypes.SprintDelete, Guid.NewGuid(), "{}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Stale);
    }
}
