using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class UpdateSubscriptionPlanCommandHandlerTests
{
    private static readonly string CoreHrBrackets =
        """[{"min_employees":1,"max_employees":50,"monthly_price":4.0,"annual_price":40.0},{"min_employees":51,"max_employees":200,"monthly_price":3.5,"annual_price":35.0}]""";

    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IModuleCatalogService _catalogService = Substitute.For<IModuleCatalogService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly UpdateSubscriptionPlanCommandHandler _handler;

    private static ModuleCatalogItem CoreHr() => new()
    {
        ModuleKey = "core_hr", Name = "Core HR", Pillar = "core_hr", Phase = "phase_1",
        PricingUnit = "per_employee", PricingReference = CoreHrBrackets, IsActive = true
    };

    private static SubscriptionPlan ExistingPlan(Guid id) => new()
    {
        Id = id, Name = "Old Name", Code = "starter", Tier = "basic",
        CompanySizeRange = "1-50", PricingUnit = "per_employee",
        IncludedModulesJson = """["core_hr"]""",
        CalculatedMonthlyPrice = 4.0m, CalculatedAnnualPrice = 40.0m,
        Currency = "USD", TrialPeriodDays = 30, UnpaidGracePeriodDays = 7,
        IsActive = true, CreatedAt = DateTimeOffset.UtcNow
    };

    public UpdateSubscriptionPlanCommandHandlerTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _handler = new UpdateSubscriptionPlanCommandHandler(
            _planRepo, _catalogService, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Handle_PlanNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);
        var command = new UpdateSubscriptionPlanCommand(id, Name: "New Name");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_NameOnlyChange_DoesNotRecalculate()
    {
        var id = Guid.NewGuid();
        var plan = ExistingPlan(id);
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);
        var command = new UpdateSubscriptionPlanCommand(id, Name: "New Name");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Name");
        result.Value.CalculatedMonthlyPrice.Should().Be(4.0m);
        await _catalogService.DidNotReceive().GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SizeRangeChange_RecalculatesPrice()
    {
        var id = Guid.NewGuid();
        var plan = ExistingPlan(id);
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        var command = new UpdateSubscriptionPlanCommand(id, CompanySizeRange: "51-200");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CalculatedMonthlyPrice.Should().Be(3.5m);
        result.Value.CompanySizeRange.Should().Be("51-200");
    }

    [Fact]
    public async Task Handle_ModuleKeysChange_UpdatesIncludedModulesAndRecalculates()
    {
        var id = Guid.NewGuid();
        var plan = ExistingPlan(id);
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        var command = new UpdateSubscriptionPlanCommand(id, ModuleKeys: ["core_hr"]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IncludedModules.Should().Contain("core_hr");
        await _catalogService.Received(1).GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownModuleKeyOnUpdate_ReturnsFailure()
    {
        var id = Guid.NewGuid();
        var plan = ExistingPlan(id);
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem>());
        // Updating size range triggers recalc; existing modules ["core_hr"] won't be found
        var command = new UpdateSubscriptionPlanCommand(id, CompanySizeRange: "51-200");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_NoBracketForSizeRange_ReturnsFailure()
    {
        var id = Guid.NewGuid();
        // Plan has only 1-50 brackets, but we'll request 201+
        var plan = ExistingPlan(id);
        _planRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(plan);
        // CoreHrBrackets covers 1-50 and 51-200 — requesting 201+ has no bracket
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        var command = new UpdateSubscriptionPlanCommand(id, CompanySizeRange: "201+");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
