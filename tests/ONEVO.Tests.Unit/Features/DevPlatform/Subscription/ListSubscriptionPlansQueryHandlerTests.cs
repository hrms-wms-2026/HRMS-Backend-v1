using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Features.DevPlatform.Subscription.Queries.ListSubscriptionPlans;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class ListSubscriptionPlansQueryHandlerTests
{
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly ListSubscriptionPlansQueryHandler _handler;

    public ListSubscriptionPlansQueryHandlerTests()
    {
        _handler = new ListSubscriptionPlansQueryHandler(_planRepo);
    }

    [Fact]
    public async Task Handle_NoPlans_ReturnsEmptyList()
    {
        _planRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan>());

        var result = await _handler.Handle(new ListSubscriptionPlansQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TwoPlans_ReturnsBothAsSummaryDtos()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Id = Guid.NewGuid(), Name = "Starter", Code = "starter", Tier = "basic",
                    CompanySizeRange = "1-50", CalculatedMonthlyPrice = 4.0m, CalculatedAnnualPrice = 40.0m,
                    Currency = "USD", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
                    IncludedModulesJson = """["core_hr"]""" },
            new() { Id = Guid.NewGuid(), Name = "Pro", Code = "pro", Tier = "professional",
                    CompanySizeRange = "51-200", CalculatedMonthlyPrice = 7.5m, CalculatedAnnualPrice = 75.0m,
                    Currency = "USD", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
                    IncludedModulesJson = """["core_hr","payroll"]""" }
        };
        _planRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(plans);

        var result = await _handler.Handle(new ListSubscriptionPlansQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(p => p.Code).Should().BeEquivalentTo("starter", "pro");
    }
}
