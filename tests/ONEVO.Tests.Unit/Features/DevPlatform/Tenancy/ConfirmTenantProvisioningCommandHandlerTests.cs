using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ConfirmTenantProvisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetProvisioningSummary;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class ConfirmTenantProvisioningCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantStatusHistoryRepository> _histories = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static readonly Guid AdminUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);

    private ConfirmTenantProvisioningCommandHandler BuildSut(bool authenticated = true)
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(authenticated);
        _currentUser.SetupGet(c => c.UserId).Returns(AdminUserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        return new ConfirmTenantProvisioningCommandHandler(
            _tenants.Object,
            _histories.Object,
            _mediator.Object,
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

    private static ProvisioningSummaryDto BuildSummary(Guid tenantId, bool canActivate)
    {
        var section = new ProvisioningSectionStatusDto(
            Complete: canActivate,
            Summary: new Dictionary<string, object?>(),
            MissingFields: Array.Empty<string>());

        return new ProvisioningSummaryDto(
            TenantId: tenantId,
            Status: "provisioning",
            Sections: new ProvisioningSectionsDto(section, section, section, section, section, section),
            CanActivate: canActivate,
            BlockingErrors: canActivate
                ? Array.Empty<ProvisioningIssueDto>()
                : new[] { new ProvisioningIssueDto("owner_invite_missing", "send an invite", "owner_invite") },
            Warnings: Array.Empty<ProvisioningIssueDto>());
    }

    private void SetupSummary(Guid tenantId, bool canActivate)
    {
        _mediator
            .Setup(m => m.Send(It.Is<GetProvisioningSummaryQuery>(q => q.TenantId == tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProvisioningSummaryDto>.Success(BuildSummary(tenantId, canActivate)));
    }

    [Fact]
    public async Task Handle_SuccessfulActivation_WritesHistory_AndPersists()
    {
        var tenant = SetupTenant(TenantStatus.Provisioning);
        SetupSummary(tenant.Id, canActivate: true);

        TenantStatusHistory? written = null;
        _histories
            .Setup(h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()))
            .Callback<TenantStatusHistory, CancellationToken>((h, _) => written = h)
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.Handle(new ConfirmTenantProvisioningCommand(tenant.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Trial);

        written.Should().NotBeNull();
        written!.TenantId.Should().Be(tenant.Id);
        written.FromStatus.Should().Be(TenantStatus.Provisioning);
        written.ToStatus.Should().Be(TenantStatus.Trial);
        written.Reason.Should().Be("provisioning_confirmed");
        written.ChangedById.Should().Be(AdminUserId);
        written.ChangedAt.Should().Be(Now);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BlockedActivation_Returns422_AndWritesNoHistory()
    {
        var tenant = SetupTenant(TenantStatus.Provisioning);
        SetupSummary(tenant.Id, canActivate: false);

        var sut = BuildSut();
        var result = await sut.Handle(new ConfirmTenantProvisioningCommand(tenant.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        tenant.Status.Should().Be(TenantStatus.Provisioning);

        _histories.Verify(h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TenantNotFound_Returns404_AndWritesNoHistory()
    {
        _tenants.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new ConfirmTenantProvisioningCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _histories.Verify(h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediator.Verify(m => m.Send(It.IsAny<GetProvisioningSummaryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TenantNotInProvisioning_Returns409_AndWritesNoHistory()
    {
        var tenant = SetupTenant(TenantStatus.Active);

        var sut = BuildSut();
        var result = await sut.Handle(new ConfirmTenantProvisioningCommand(tenant.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _histories.Verify(h => h.AddAsync(It.IsAny<TenantStatusHistory>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediator.Verify(m => m.Send(It.IsAny<GetProvisioningSummaryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
