using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateTenantSubscription;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class UpdateTenantSubscriptionCommandHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ITenantSubscriptionRepository _subscriptionRepo = Substitute.For<ITenantSubscriptionRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IBillingAuditLogRepository _auditLogRepo = Substitute.For<IBillingAuditLogRepository>();
    private readonly ISeatEntitlementService _seats = Substitute.For<ISeatEntitlementService>();
    private readonly ICurrentPlatformUserContext _platformUser = Substitute.For<ICurrentPlatformUserContext>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly UpdateTenantSubscriptionCommandHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private TenantSubscription _subscription = null!;

    public UpdateTenantSubscriptionCommandHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _platformUser.UserId.Returns(_adminId);
        _seats.EvaluateAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new SeatDecision(SeatDecisionStatus.Approved, 20, 4, 0, 16, false, false, "ok"));
        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Name = "Unicom", Slug = "unicom" });
        _subscription = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PlanId = Guid.NewGuid(),
            Status = "trialing",
            CommercialModel = "standard",
            TrialStartDate = _now.AddDays(-3),
            TrialEndDate = _now.AddDays(11),
            CreatedAt = _now.AddDays(-3),
        };
        _subscriptionRepo.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(_ => _subscription);

        _handler = new UpdateTenantSubscriptionCommandHandler(
            _tenantRepo, _subscriptionRepo, _planRepo, _auditLogRepo, _seats, _platformUser, _unitOfWork, _clock);
    }

    [Fact]
    public async Task SettingSeats_OnUnsetPolicy_AlsoSetsOverageFalse_AndReturnsUsedSeats()
    {
        var result = await _handler.Handle(
            new UpdateTenantSubscriptionCommand(_tenantId, 20, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscription.IncludedSeats.Should().Be(20);
        _subscription.OverageAllowed.Should().BeFalse();
        result.Value!.IncludedSeats.Should().Be(20);
        result.Value.UsedSeats.Should().Be(4);
        result.Value.IsTest.Should().BeFalse();
        await _auditLogRepo.Received(1).AddAsync(
            Arg.Is<BillingAuditLog>(l =>
                l.Action == "subscription.allocation_updated" &&
                l.TenantId == _tenantId &&
                l.ActorAdminUserId == _adminId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverageOnly_KeepsSeats()
    {
        _subscription.IncludedSeats = 5;
        _subscription.OverageAllowed = false;

        var result = await _handler.Handle(
            new UpdateTenantSubscriptionCommand(_tenantId, null, true, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscription.IncludedSeats.Should().Be(5);
        _subscription.OverageAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkingAsTest_ActivatesAndClearsTrial()
    {
        var result = await _handler.Handle(
            new UpdateTenantSubscriptionCommand(_tenantId, null, null, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscription.CommercialModel.Should().Be("test");
        _subscription.Status.Should().Be("active");
        _subscription.TrialEndDate.Should().BeNull();
        _subscription.AccessEndsAt.Should().BeNull();
        result.Value!.IsTest.Should().BeTrue();
    }

    [Fact]
    public async Task UnmarkingTest_RestoresSubscriptionModel_WithoutChangingStatus()
    {
        _subscription.CommercialModel = "test";
        _subscription.Status = "active";

        var result = await _handler.Handle(
            new UpdateTenantSubscriptionCommand(_tenantId, null, null, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _subscription.CommercialModel.Should().Be("subscription");
        _subscription.Status.Should().Be("active");
    }

    [Fact]
    public async Task MissingTenant_ReturnsNotFound()
    {
        var result = await _handler.Handle(
            new UpdateTenantSubscriptionCommand(Guid.NewGuid(), 5, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, null, null, false)]
    [InlineData(100_001, null, null, false)]
    [InlineData(null, null, null, false)]
    [InlineData(0, null, null, true)]
    [InlineData(null, null, true, true)]
    public void Validator_EnforcesRange_AndAtLeastOneField(int? seats, bool? overage, bool? isTest, bool valid)
    {
        var validator = new UpdateTenantSubscriptionCommandValidator();

        var outcome = validator.Validate(new UpdateTenantSubscriptionCommand(_tenantId, seats, overage, isTest));

        outcome.IsValid.Should().Be(valid);
    }
}
