using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.GetSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class GetSubscriptionPlanQueryHandlerTests
{
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly GetSubscriptionPlanQueryHandler _handler;

    public GetSubscriptionPlanQueryHandlerTests()
    {
        _handler = new GetSubscriptionPlanQueryHandler(_planRepo);
    }

    [Fact]
    public async Task Handle_PlanNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var result = await _handler.Handle(new GetSubscriptionPlanQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ExistingPlan_ReturnsDetailDto()
    {
        var id = Guid.NewGuid();
        var plan = new SubscriptionPlan
        {
            Id = id, Name = "Starter", Code = "starter", Tier = "basic",
            CompanySizeRange = "1-50", PricingUnit = "per_employee",
            IncludedModulesJson = """["core_hr"]""",
            CalculatedMonthlyPrice = 4.0m, CalculatedAnnualPrice = 40.0m,
            Currency = "USD", TrialPeriodDays = 30, UnpaidGracePeriodDays = 7,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow
        };
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);

        var result = await _handler.Handle(new GetSubscriptionPlanQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(id);
        result.Value.Code.Should().Be("starter");
        result.Value.IncludedModules.Should().Contain("core_hr");
    }
}
