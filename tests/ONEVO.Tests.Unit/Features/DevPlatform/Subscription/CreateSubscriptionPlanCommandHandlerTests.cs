using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class CreateSubscriptionPlanCommandHandlerTests
{
    private static readonly string CoreHrBrackets =
        """[{"min_employees":1,"max_employees":50,"monthly_price":4.0,"annual_price":40.0}]""";

    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IModuleCatalogService _catalogService = Substitute.For<IModuleCatalogService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CreateSubscriptionPlanCommandHandler _handler;

    private static ModuleCatalogItem CoreHr() => new()
    {
        ModuleKey = "core_hr", Name = "Core HR", Pillar = "core_hr", Phase = "phase_1",
        PricingUnit = "per_employee", PricingReference = CoreHrBrackets, IsActive = true
    };

    public CreateSubscriptionPlanCommandHandlerTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _handler = new CreateSubscriptionPlanCommandHandler(
            _planRepo, _catalogService, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflict()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(true);
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "1-50", ["core_hr"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_UnknownModuleKey_ReturnsFailure()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(false);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem>());
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "1-50", ["ghost_module"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("ghost_module");
    }

    [Fact]
    public async Task Handle_ValidCommand_SavesPlanAndReturnsDetailDto()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(false);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "1-50", ["core_hr"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("starter");
        result.Value.CalculatedMonthlyPrice.Should().Be(4.0m);
        result.Value.EffectiveMonthlyPrice.Should().Be(4.0m);
        await _planRepo.Received(1).AddAsync(Arg.Any<SubscriptionPlan>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithOverridePrice_EffectivePriceUsesOverride()
    {
        _planRepo.ExistsByCodeAsync("pro", Arg.Any<CancellationToken>()).Returns(false);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        var command = new CreateSubscriptionPlanCommand(
            "Pro", "pro", "professional", "1-50", ["core_hr"],
            "USD", OverrideMonthlyPrice: 9.99m, OverrideAnnualPrice: 99.99m, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EffectiveMonthlyPrice.Should().Be(9.99m);
        result.Value.EffectiveAnnualPrice.Should().Be(99.99m);
        result.Value.CalculatedMonthlyPrice.Should().Be(4.0m);
    }

    [Fact]
    public async Task Handle_InactiveLegacyModuleKey_ReturnsModuleKeyInvalid()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(false);
        var legacy = CoreHr();
        legacy.ModuleKey = "tasks";
        legacy.IsActive = false;
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr(), legacy });
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "1-50", ["core_hr", "tasks"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("module_key_invalid");
        result.Error.Should().Contain("tasks");
        await _planRepo.DidNotReceive().AddAsync(Arg.Any<SubscriptionPlan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BaselineModuleKey_ReturnsModuleKeyInvalid()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(false);
        var auth = CoreHr();
        auth.ModuleKey = "auth";
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr(), auth });
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "1-50", ["core_hr", "auth"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("module_key_invalid");
        result.Error.Should().Contain("auth");
    }

    [Fact]
    public async Task Handle_DuplicateAndPaddedKeys_StoresTrimmedDistinctKeys()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(false);
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        SubscriptionPlan? saved = null;
        await _planRepo.AddAsync(Arg.Do<SubscriptionPlan>(p => saved = p), Arg.Any<CancellationToken>());
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "1-50", [" core_hr", "core_hr"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved!.IncludedModulesJson.Should().Be("""["core_hr"]""");
    }

    [Fact]
    public async Task Handle_NoBracketForSizeRange_ReturnsFailure()
    {
        _planRepo.ExistsByCodeAsync("starter", Arg.Any<CancellationToken>()).Returns(false);
        // CoreHr only has bracket for 1-50, but command requests 51-200
        _catalogService.GetByCatalogKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleCatalogItem> { CoreHr() });
        var command = new CreateSubscriptionPlanCommand(
            "Starter", "starter", "basic", "51-200", ["core_hr"],
            "USD", null, null, null, 30, 7);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
