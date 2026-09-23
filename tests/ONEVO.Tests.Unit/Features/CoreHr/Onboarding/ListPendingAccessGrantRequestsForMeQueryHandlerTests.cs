using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class ListPendingAccessGrantRequestsForMeQueryHandlerTests
{
    private readonly Mock<IAccessGrantRequestRepository> _repository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ListPendingAccessGrantRequestsForMeQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
    }

    private ListPendingAccessGrantRequestsForMeQueryHandler CreateHandler()
        => new(_repository.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_ReturnsOnlyPendingRequests_WithResolvedNames()
    {
        var onboardingId = Guid.NewGuid();
        var positionChangeId = Guid.NewGuid();
        var requestedAt = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

        // ListPendingAsync is the tenant-scoped Pending-only listing. Approved/Rejected rows
        // never come back from the repository, matching the mix of statuses that would exist
        // in storage. Onboarding rows have a null EmployeeId, so EmployeeName stays null.
        IReadOnlyList<PendingAccessGrantRequestResponse> pending =
        [
            new(
                onboardingId,
                AccessGrantActionType.EmployeeOnboarding,
                EmployeeName: null,
                TargetPositionName: "Software Engineer",
                ChangeReason: null,
                RequestedByName: "Riya Starter",
                RequestedAt: requestedAt,
                InvitedFullName: "Ada Lovelace"),
            new(
                positionChangeId,
                AccessGrantActionType.PositionChange,
                EmployeeName: "Jane Doe",
                TargetPositionName: "Engineering Manager",
                ChangeReason: "Promotion",
                RequestedByName: "Riya Starter",
                RequestedAt: requestedAt.AddMinutes(5)),
        ];

        _repository
            .Setup(r => r.ListPendingAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await CreateHandler().Handle(new ListPendingAccessGrantRequestsForMeQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Count);

        var onboarding = Assert.Single(result.Value, x => x.Id == onboardingId);
        Assert.Equal(AccessGrantActionType.EmployeeOnboarding, onboarding.ActionType);
        Assert.Null(onboarding.EmployeeName);
        Assert.Equal("Ada Lovelace", onboarding.InvitedFullName);
        Assert.Equal("Software Engineer", onboarding.TargetPositionName);
        Assert.Null(onboarding.ChangeReason);
        Assert.Equal("Riya Starter", onboarding.RequestedByName);
        Assert.Equal(requestedAt, onboarding.RequestedAt);

        var positionChange = Assert.Single(result.Value, x => x.Id == positionChangeId);
        Assert.Equal(AccessGrantActionType.PositionChange, positionChange.ActionType);
        Assert.Equal("Jane Doe", positionChange.EmployeeName);
        Assert.Equal("Engineering Manager", positionChange.TargetPositionName);
        Assert.Equal("Promotion", positionChange.ChangeReason);
        Assert.Equal("Riya Starter", positionChange.RequestedByName);

        _repository.Verify(r => r.ListPendingAsync(_tenantId, _userId, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.ListPendingAsync(It.Is<Guid>(id => id != _tenantId), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PassesCurrentUserId_SoCallersOwnSubmissionsAreExcluded()
    {
        _repository
            .Setup(r => r.ListPendingAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateHandler().Handle(new ListPendingAccessGrantRequestsForMeQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.ListPendingAsync(_tenantId, _userId, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.ListPendingAsync(_tenantId, It.Is<Guid>(id => id != _userId), It.IsAny<CancellationToken>()), Times.Never);
    }
}
