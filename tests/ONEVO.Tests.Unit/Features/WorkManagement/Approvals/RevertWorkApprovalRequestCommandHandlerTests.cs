using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class RevertWorkApprovalRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IApprovalActionReverterRegistry> _reverters = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly FakeUnitOfWork _uow = new();

    public RevertWorkApprovalRequestCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Caller);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
    }

    private RevertWorkApprovalRequestCommandHandler Handler() => new(
        _currentUser.Object, _identity.Object, _requests.Object, _reverters.Object, _notifications.Object, _uow);

    private static WorkApprovalRequest ApprovedRequest(Guid decidedBy, DateTimeOffset? decidedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ActionType = WorkActionTypes.ModuleEdit, TargetType = WorkTargetTypes.Module,
        TargetId = Guid.NewGuid(), RequestedByEmployeeId = Requester, Status = WorkApprovalRequestStatuses.Approved,
        DecidedByEmployeeId = decidedBy, DecidedAt = decidedAt ?? DateTimeOffset.UtcNow,
    };

    private static WorkApprovalRequest RejectedRequest(Guid decidedBy, DateTimeOffset? decidedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ActionType = WorkActionTypes.ModuleEdit, TargetType = WorkTargetTypes.Module,
        TargetId = Guid.NewGuid(), RequestedByEmployeeId = Requester, Status = WorkApprovalRequestStatuses.Rejected,
        DecidedByEmployeeId = decidedBy, DecidedAt = decidedAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Handle_CallerIsNotTheDecider_ReturnsForbidden()
    {
        var request = ApprovedRequest(decidedBy: Guid.NewGuid());
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_WindowExpired_ReturnsConflict()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-31));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_RejectedRequest_FlipsToPendingWithNoReverterNeeded()
    {
        var request = RejectedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
        request.RevertedAt.Should().NotBeNull();
        request.RevertedByEmployeeId.Should().Be(Caller);
        request.DecidedByEmployeeId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ApprovedRequest_NoReverterRegistered_ReturnsUnprocessable()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _reverters.Setup(x => x.Find(request.ActionType)).Returns((IApprovalActionReverter?)null);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_ApprovedRequest_ReverterReturnsConflict_LeavesRequestApproved()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var reverter = new Mock<IApprovalActionReverter>();
        reverter.Setup(x => x.RevertAsync(It.IsAny<ApprovalRevertContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevertOutcome.Conflict("already moved on"));
        _reverters.Setup(x => x.Find(request.ActionType)).Returns(reverter.Object);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        request.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
    }

    [Fact]
    public async Task Handle_ApprovedRequest_ReverterSucceeds_RebaselinesTargetUpdatedAtSnapshot()
    {
        var request = ApprovedRequest(decidedBy: Caller, decidedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        var freshUpdatedAt = DateTimeOffset.UtcNow;
        var reverter = new Mock<IApprovalActionReverter>();
        reverter.Setup(x => x.RevertAsync(It.IsAny<ApprovalRevertContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RevertOutcome.Reverted(() => freshUpdatedAt));
        _reverters.Setup(x => x.Find(request.ActionType)).Returns(reverter.Object);

        var result = await Handler().Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
        request.TargetUpdatedAtSnapshot.Should().Be(freshUpdatedAt);
        request.UndoStateJson.Should().BeNull();
        request.AppliedPayloadJson.Should().BeNull();
        _uow.SaveCallCount.Should().Be(2);
    }
}
