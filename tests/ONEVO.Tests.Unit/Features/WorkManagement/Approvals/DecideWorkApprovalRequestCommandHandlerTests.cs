using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class DecideWorkApprovalRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();        // position holder
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IApprovalActionApplierRegistry> _appliers = new();
    private readonly Mock<IApprovalActionApplier> _applier = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly WorkApprovalRequest _request;

    public DecideWorkApprovalRequestCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = A },
            }));
        _request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = WorkActionTypes.TaskEdit,
            TargetType = WorkTargetTypes.Task, TargetId = Guid.NewGuid(), TargetTitle = "Audit events",
            PositionObjectiveId = _p, ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = A,
            RequestedByEmployeeId = Requester, PayloadJson = "{\"title\":\"old\"}", Status = WorkApprovalRequestStatuses.Pending
        };
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, _request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_request);
        _applier.SetupGet(x => x.ActionType).Returns(WorkActionTypes.TaskEdit);
        _appliers.Setup(x => x.Find(WorkActionTypes.TaskEdit)).Returns(_applier.Object);
    }

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private DecideWorkApprovalRequestCommandHandler Build() => new(
        _currentUser.Object, _identity.Object, _requests.Object, _hierarchy.Object, _appliers.Object, _notifications.Object, _uow);

    private static DecideWorkApprovalRequestCommand Cmd(Guid id, WorkApprovalDecision d, string? payload = null)
        => new(id, d, payload, "ok");

    [Fact]
    public async Task Approve_ByHolder_AppliesEditedPayload_MarksApproved_NotifiesRequester()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.Is<ApprovalApplyContext>(c => c.PayloadJson == "{\"title\":\"new\"}" && c.DeciderEmployeeId == A), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.Applied());

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve, "{\"title\":\"new\"}"), default);

        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
        _request.PayloadJson.Should().Be("{\"title\":\"old\"}");
        _request.AppliedPayloadJson.Should().Be("{\"title\":\"new\"}");
        _request.DecidedByEmployeeId.Should().Be(A);
        _uow.SaveCallCount.Should().Be(1);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Approved && e.RecipientEmployeeIds.Single() == Requester), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Approve_with_edited_payload_keeps_requested_payload_and_stores_applied()
    {
        Caller(A);
        _request.PayloadJson = """{"Title":"A"}""";
        _applier.Setup(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(ApplyOutcome.Applied());

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve, """{"Title":"B"}"""), default);

        result.IsSuccess.Should().BeTrue();
        _request.PayloadJson.Should().Be("""{"Title":"A"}""");
        _request.AppliedPayloadJson.Should().Be("""{"Title":"B"}""");
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
    }

    [Fact]
    public async Task Approve_without_edits_leaves_applied_payload_null()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(ApplyOutcome.Applied());

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default);

        result.IsSuccess.Should().BeTrue();
        _request.AppliedPayloadJson.Should().BeNull();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
    }

    [Fact]
    public async Task Approve_StaleTarget_MarksStale_AppliesNothingElse()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(ApplyOutcome.Stale);

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default);

        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Stale);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e => e.Kind == WorkNotificationKinds.Stale), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Approve_InvalidPayload_Returns422_StaysPending()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(ApplyOutcome.Invalid("bad title"));

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default);

        result.StatusCode.Should().Be(422);
        result.Error.Should().Be("bad title");
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Approve_NoApplierRegistered_Returns422()
    {
        Caller(A);
        _appliers.Setup(x => x.Find(WorkActionTypes.TaskEdit)).Returns((IApprovalActionApplier?)null);
        (await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default)).StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Reject_ByHolder_MarksRejected_NeverCallsApplier()
    {
        Caller(A);
        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Reject), default);

        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Rejected);
        _applier.Verify(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(WorkApprovalDecision.Approve)]
    [InlineData(WorkApprovalDecision.Reject)]
    public async Task Decide_ByNonHolder_Forbidden(WorkApprovalDecision decision)
    {
        Caller(Stranger);
        (await Build().Handle(Cmd(_request.Id, decision), default)).StatusCode.Should().Be(403);
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
    }

    [Fact]
    public async Task Cancel_OnlyRequester_NotifiesApprover()
    {
        Caller(A);
        (await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Cancel), default)).StatusCode.Should().Be(403);

        Caller(Requester);
        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Cancel), default);
        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Cancelled);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Cancelled && e.RecipientEmployeeIds.Single() == A), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task AlreadyDecided_Returns409()
    {
        Caller(A);
        _request.Status = WorkApprovalRequestStatuses.Approved;
        (await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Reject), default)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UnknownRequest_Returns404()
    {
        Caller(A);
        (await Build().Handle(Cmd(Guid.NewGuid(), WorkApprovalDecision.Approve), default)).StatusCode.Should().Be(404);
    }
}
