using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantSubscription;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class GetTenantSubscriptionQueryHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ITenantSubscriptionRepository _subscriptionRepo = Substitute.For<ITenantSubscriptionRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly GetTenantSubscriptionQueryHandler _handler;
    private readonly DateTimeOffset _now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();

    public GetTenantSubscriptionQueryHandlerTests()
    {
        _clock.UtcNow.Returns(_now);
        _handler = new GetTenantSubscriptionQueryHandler(
            _tenantRepo,
            _subscriptionRepo,
            _planRepo,
            _clock);
    }

    [Fact]
    public async Task Handle_ActiveSubscription_ReturnsDetail()
    {
        var tenant = new Tenant
        {
            Id = _tenantId,
            Name = "Acme Co",
            Slug = "acme-co"
        };
        var subscription = BuildSubscription("active", trialEnd: null, accessEndsAt: null);
        var plan = new SubscriptionPlan
        {
            Id = _planId,
            Name = "Starter",
            Code = "starter",
            IsActive = true
        };

        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _subscriptionRepo.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(subscription);
        _planRepo.GetByIdAsync(_planId, Arg.Any<CancellationToken>()).Returns(plan);

        var result = await _handler.Handle(new GetTenantSubscriptionQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(_tenantId);
        result.Value.TenantName.Should().Be("Acme Co");
        result.Value.PlanName.Should().Be("Starter");
        result.Value.PlanCode.Should().Be("starter");
        result.Value.Status.Should().Be("active");
        result.Value.Amount.Should().Be(100m);
        result.Value.IsActiveAccess.Should().BeTrue();
        result.Value.IsPastDue.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TrialingSubscription_ComputesTrialFlags()
    {
        var tenant = new Tenant { Id = _tenantId, Name = "Trial Co", Slug = "trial-co" };
        var trialEnd = _now.AddDays(7);
        var subscription = BuildSubscription("trialing", trialEnd, accessEndsAt: null);

        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _subscriptionRepo.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(subscription);
        _planRepo.GetByIdAsync(_planId, Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var result = await _handler.Handle(new GetTenantSubscriptionQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsInTrial.Should().BeTrue();
        result.Value.IsActiveAccess.Should().BeTrue();
        result.Value.TrialEndsAt.Should().Be(trialEnd);
    }

    [Fact]
    public async Task Handle_PastDueSubscription_ComputesGraceFlags()
    {
        var tenant = new Tenant { Id = _tenantId, Name = "Past Due Co", Slug = "past-due-co" };
        var accessEndsAt = _now.AddDays(5);
        var subscription = BuildSubscription("past_due", trialEnd: null, accessEndsAt);

        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _subscriptionRepo.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(subscription);
        _planRepo.GetByIdAsync(_planId, Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var result = await _handler.Handle(new GetTenantSubscriptionQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPastDue.Should().BeTrue();
        result.Value.IsInGracePeriod.Should().BeTrue();
        result.Value.IsActiveAccess.Should().BeTrue();
        result.Value.GraceEndsAt.Should().Be(accessEndsAt);
        result.Value.DaysUntilAccessEnds.Should().Be(5);
    }

    [Fact]
    public async Task Handle_MissingTenant_ReturnsNotFound()
    {
        var result = await _handler.Handle(new GetTenantSubscriptionQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_MissingSubscription_ReturnsNotFound()
    {
        _tenantRepo.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Name = "No Sub", Slug = "no-sub" });

        var result = await _handler.Handle(new GetTenantSubscriptionQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Subscription");
    }

    private TenantSubscription BuildSubscription(
        string status,
        DateTimeOffset? trialEnd,
        DateTimeOffset? accessEndsAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PlanId = _planId,
            BillingCycle = "monthly",
            Status = status,
            BillingCurrency = "USD",
            CalculatedMonthlyPrice = 100m,
            CalculatedAnnualPrice = 1000m,
            CurrentPeriodStart = DateOnly.FromDateTime(_now.UtcDateTime),
            CurrentPeriodEnd = DateOnly.FromDateTime(_now.UtcDateTime.AddDays(20)),
            ContractStartDate = DateOnly.FromDateTime(_now.UtcDateTime),
            TrialEndDate = trialEnd,
            AccessEndsAt = accessEndsAt,
            UnpaidGracePeriodDays = 7,
            CreatedAt = _now
        };
}
