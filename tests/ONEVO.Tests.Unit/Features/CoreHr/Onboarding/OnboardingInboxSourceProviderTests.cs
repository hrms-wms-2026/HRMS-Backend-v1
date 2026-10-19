using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Inbox;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class OnboardingInboxSourceProviderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Hr = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IOnboardingInboxRepository> _rows = new();
    private readonly Mock<ISeatIncreaseRequestRepository> _seatRequests = new();
    private readonly Mock<ITenantSubscriptionRepository> _subscriptions = new();
    private readonly HashSet<string> _permissions = new();

    public OnboardingInboxSourceProviderTests()
    {
        _currentUser.SetupGet(u => u.UserId).Returns(Me);
        _currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        _currentUser.Setup(u => u.HasPermission(It.IsAny<string>())).Returns((string code) => _permissions.Contains(code));
        _rows.Setup(r => r.ListWaitingForSeatNamesAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(["Ann One", "Ben Two", "Cy Three"]);
    }

    private OnboardingInboxSourceProvider Provider() => new(
        _currentUser.Object, _rows.Object, _seatRequests.Object, _subscriptions.Object,
        new OnboardingApprovers(Mock.Of<IPermissionRepository>(), Mock.Of<IUserRepository>(), Mock.Of<IEmployeeRepository>(), new FakeDateTimeProvider()),
        Mock.Of<ISender>(), new FakeUnitOfWork(), new FakeDateTimeProvider { UtcNow = Now });

    private static OnboardingInboxRow Row(string kind, Guid requestedBy, string status = "pending") => new(
        kind, Guid.NewGuid(), status, requestedBy, "Hana HR", Guid.NewGuid(), null, null, Now.AddHours(-1), null, null,
        "New Person", Guid.NewGuid(), "Engineer", "Engineering", null, kind == OnboardingInboxKinds.SeatIncrease ? 1 : null, 50, null);

    private void RowsAre(params OnboardingInboxRow[] rows)
        => _rows.Setup(r => r.ListAsync(TenantId, Me, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Only_the_permission_holders_are_asked_for_pending_requests()
    {
        _permissions.Add(OnboardingApprovers.SeatIncreasePermission);
        RowsAre();

        await Provider().ListAsync(TenantId, new InboxQuery(), Guid.NewGuid(), default);

        _rows.Verify(r => r.ListAsync(TenantId, Me, false, true, Now.AddDays(-OnboardingInboxSourceProvider.DecidedHistoryDays), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task An_approver_receives_pending_requests_with_approve_and_reject()
    {
        _permissions.Add(OnboardingApprovers.PositionApprovalPermission);
        _permissions.Add(OnboardingApprovers.SeatIncreasePermission);
        var position = Row(OnboardingInboxKinds.PositionApproval, Hr, "Pending");
        var seat = Row(OnboardingInboxKinds.SeatIncrease, Hr);
        RowsAre(position, seat);

        var items = await Provider().ListAsync(TenantId, new InboxQuery(), Guid.NewGuid(), default);

        items.Should().HaveCount(2).And.OnlyContain(i => i.Direction == InboxDirections.Received && i.IsPending
            && i.AvailableActions.Select(a => a.Key).SequenceEqual(new[] { "approve", "reject" }));
    }

    [Fact]
    public async Task The_requester_of_a_position_approval_sees_it_as_sent_without_actions()
    {
        _permissions.Add(OnboardingApprovers.PositionApprovalPermission);
        RowsAre(Row(OnboardingInboxKinds.PositionApproval, Me, "Pending"));

        var item = (await Provider().ListAsync(TenantId, new InboxQuery(), Guid.NewGuid(), default)).Single();

        item.Direction.Should().Be(InboxDirections.Sent);
        item.AvailableActions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_billing_manager_can_approve_a_seat_increase_they_raised_themselves()
    {
        _permissions.Add(OnboardingApprovers.SeatIncreasePermission);
        RowsAre(Row(OnboardingInboxKinds.SeatIncrease, Me));

        var item = (await Provider().ListAsync(TenantId, new InboxQuery(), Guid.NewGuid(), default)).Single();

        item.Direction.Should().Be(InboxDirections.Sent);
        item.AvailableActions.Select(a => a.Key).Should().Equal("approve", "reject");
    }

    [Fact]
    public async Task Approving_a_seat_increase_adds_the_seats_to_the_subscription()
    {
        _permissions.Add(OnboardingApprovers.SeatIncreasePermission);
        var row = Row(OnboardingInboxKinds.SeatIncrease, Hr);
        var request = new SeatIncreaseRequest { Id = row.Id, TenantId = TenantId, RequestedSeats = 2, RequestedByUserId = Hr };
        var subscription = new TenantSubscription { TenantId = TenantId, IncludedSeats = 50, OverageAllowed = false };
        _rows.Setup(r => r.GetAsync(TenantId, OnboardingInboxKinds.SeatIncrease, row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _seatRequests.Setup(r => r.GetTrackedByIdAsync(TenantId, row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        _subscriptions.Setup(r => r.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(subscription);

        var result = await Provider().ExecuteActionAsync(TenantId, OnboardingInboxKinds.SeatIncrease, row.Id, "approve", null, Guid.NewGuid(), default);

        result.IsSuccess.Should().BeTrue(result.Error);
        // Three people are waiting, so the one request adds three seats (more than the two it was raised with).
        subscription.IncludedSeats.Should().Be(53);
        request.Status.Should().Be(SeatIncreaseRequestStatus.Approved);
        request.DecidedByUserId.Should().Be(Me);
    }

    [Fact]
    public async Task Someone_without_billing_manage_cannot_decide_a_seat_increase()
    {
        var result = await Provider().ExecuteActionAsync(TenantId, OnboardingInboxKinds.SeatIncrease, Guid.NewGuid(), "approve", null, Guid.NewGuid(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
