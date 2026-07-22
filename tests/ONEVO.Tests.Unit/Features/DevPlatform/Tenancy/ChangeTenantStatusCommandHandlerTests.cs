using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class ChangeTenantStatusCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantStatusHistoryRepository> _histories = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly Guid AdminUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private ChangeTenantStatusCommandHandler BuildSut(bool authenticated = true)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(authenticated);
        _currentUser.SetupGet(c => c.UserId).Returns(AdminUserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        return new ChangeTenantStatusCommandHandler(
            _tenants.Object,
            _histories.Object,
            _currentUser.Object,
            _uow.Object,
            _clock.Object);
    }

    private Tenant SetupTenant(TenantStatus status)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "T", Slug = "t", Status = status };
        _tenants.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        return tenant;
    }

    [Fact]
    public async Task Handle_Suspend_WritesStatusHistory_AndPersists()
    {
        var tenant = SetupTenant(TenantStatus.Active);
        TenantStatusHistory? written = null;
        _histories
            .Setup(h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()))
            .Callback<TenantStatusHistory, CancellationToken>((h, _) => written = h)
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ChangeTenantStatusCommand(tenant.Id, "suspend", "billing overdue"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Suspended);

        written.Should().NotBeNull();
        written!.TenantId.Should().Be(tenant.Id);
        written.FromStatus.Should().Be(TenantStatus.Active);
        written.ToStatus.Should().Be(TenantStatus.Suspended);
        written.Reason.Should().Be("billing overdue");
        written.ChangedById.Should().Be(AdminUserId);
        written.ChangedAt.Should().Be(Now);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Unsuspend_WritesHistory_WithNullReason()
    {
        var tenant = SetupTenant(TenantStatus.Suspended);
        TenantStatusHistory? written = null;
        _histories
            .Setup(h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()))
            .Callback<TenantStatusHistory, CancellationToken>((h, _) => written = h)
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ChangeTenantStatusCommand(tenant.Id, "unsuspend", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        written.Should().NotBeNull();
        written!.FromStatus.Should().Be(TenantStatus.Suspended);
        written.ToStatus.Should().Be(TenantStatus.Active);
        written.Reason.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoOpTransition_Returns409_AndWritesNoHistory()
    {
        // "activate" on an already-active tenant maps to Active -> Active,
        // which IsValidTransition rejects: no-ops never write history.
        var tenant = SetupTenant(TenantStatus.Active);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ChangeTenantStatusCommand(tenant.Id, "activate", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _histories.Verify(
            h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidTransition_Returns409_AndWritesNoHistory()
    {
        var tenant = SetupTenant(TenantStatus.Cancelled);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ChangeTenantStatusCommand(tenant.Id, "suspend", "why"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _histories.Verify(
            h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TenantNotFound_Returns404_AndWritesNoHistory()
    {
        _tenants.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ChangeTenantStatusCommand(Guid.NewGuid(), "suspend", "why"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _histories.Verify(
            h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Unauthenticated_Returns403_AndWritesNoHistory()
    {
        var sut = BuildSut(authenticated: false);
        var result = await sut.Handle(
            new ChangeTenantStatusCommand(Guid.NewGuid(), "suspend", "why"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _histories.Verify(
            h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
