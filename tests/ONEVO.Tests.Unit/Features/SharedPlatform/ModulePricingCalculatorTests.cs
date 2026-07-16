using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform;

public class ModulePricingCalculatorTests
{
    private static readonly string CoreHrBrackets =
        """[{"min_employees":0,"max_employees":50,"monthly_price":4.0,"annual_price":40.0},{"min_employees":51,"max_employees":200,"monthly_price":3.5,"annual_price":35.0},{"min_employees":201,"max_employees":-1,"monthly_price":3.0,"annual_price":30.0}]""";

    private static readonly string WorkMgmtBrackets =
        """[{"min_employees":0,"max_employees":50,"monthly_price":4.5,"annual_price":45.0},{"min_employees":51,"max_employees":200,"monthly_price":4.0,"annual_price":40.0},{"min_employees":201,"max_employees":-1,"monthly_price":3.5,"annual_price":35.0}]""";

    private static readonly string ChatAiBrackets =
        """[{"min_employees":0,"max_employees":50,"monthly_price":2.0,"annual_price":20.0},{"min_employees":51,"max_employees":200,"monthly_price":1.5,"annual_price":15.0},{"min_employees":201,"max_employees":-1,"monthly_price":1.0,"annual_price":10.0}]""";

    private static ModuleCatalogItem CoreHr(bool active = true) => new()
    {
        ModuleKey = "core_hr",
        Name = "Core HR",
        Pillar = "core_hr",
        Phase = "phase_1",
        PricingUnit = "per_employee",
        PricingReference = CoreHrBrackets,
        IsActive = active
    };

    private static ModuleCatalogItem WorkManagement(bool active = true) => new()
    {
        ModuleKey = "work_management",
        Name = "Work Management",
        Pillar = "work_management",
        Phase = "phase_1",
        PricingUnit = "per_employee",
        PricingReference = WorkMgmtBrackets,
        IsActive = active
    };

    private static ModuleCatalogItem ChatAi(bool active = true) => new()
    {
        ModuleKey = "chat_ai",
        Name = "Chat AI",
        Pillar = "ai",
        Phase = "phase_1",
        PricingUnit = "per_employee",
        PricingReference = ChatAiBrackets,
        IsActive = active
    };

    [Fact]
    public void Calculate_SizeRange_51_200_CoreHrAndWorkMgmt_ReturnsMonthly7Point50()
    {
        // core_hr @ 51-200 => 3.5, work_management @ 51-200 => 4.0 => total 7.50
        var catalog = new[] { CoreHr(), WorkManagement() };
        var selectedKeys = new[] { "core_hr", "work_management" };
        var calculator = new ModulePricingCalculator();

        var result = calculator.Calculate(catalog, selectedKeys, "51-200");

        result.CalculatedMonthlyPrice.Should().Be(7.50m);
        result.CalculatedAnnualPrice.Should().Be(75.00m);
    }

    [Fact]
    public void Calculate_SizeRange_1_50_CoreHrAndWorkMgmt_ReturnsMonthly8Point50()
    {
        // core_hr @ 1-50 => 4.0, work_management @ 1-50 => 4.5 => total 8.50
        var catalog = new[] { CoreHr(), WorkManagement() };
        var selectedKeys = new[] { "core_hr", "work_management" };
        var calculator = new ModulePricingCalculator();

        var result = calculator.Calculate(catalog, selectedKeys, "1-50");

        result.CalculatedMonthlyPrice.Should().Be(8.50m);
        result.CalculatedAnnualPrice.Should().Be(85.00m);
    }

    [Fact]
    public void Calculate_InactiveModule_ThrowsInvalidOperationException()
    {
        var catalog = new[] { CoreHr(active: false), WorkManagement() };
        var selectedKeys = new[] { "core_hr", "work_management" };
        var calculator = new ModulePricingCalculator();

        var act = () => calculator.Calculate(catalog, selectedKeys, "51-200");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*core_hr*not active*");
    }

    [Fact]
    public void Calculate_MixedPricingUnits_ThrowsInvalidOperationException()
    {
        var flat = new ModuleCatalogItem
        {
            ModuleKey = "flat_module",
            Name = "Flat Module",
            Pillar = "other",
            Phase = "phase_1",
            PricingUnit = "flat_fee",
            PricingReference = CoreHrBrackets,
            IsActive = true
        };

        var catalog = new[] { CoreHr(), flat };
        var selectedKeys = new[] { "core_hr", "flat_module" };
        var calculator = new ModulePricingCalculator();

        var act = () => calculator.Calculate(catalog, selectedKeys, "51-200");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mixed pricing units*");
    }

    [Fact]
    public void Calculate_AiPlanWith201Plus_ReturnsCombinedPrice()
    {
        // core_hr @ 201+ => 3.0, chat_ai @ 201+ => 1.0 => total monthly 4.0
        var catalog = new[] { CoreHr(), ChatAi() };
        var selectedKeys = new[] { "core_hr", "chat_ai" };
        var calculator = new ModulePricingCalculator();

        var result = calculator.Calculate(catalog, selectedKeys, "201+");

        result.CalculatedMonthlyPrice.Should().Be(4.0m);
        result.CalculatedAnnualPrice.Should().Be(40.0m);
    }

    [Fact]
    public void Calculate_EmptyModuleList_ThrowsInvalidOperationException()
    {
        var catalog = new[] { CoreHr(), WorkManagement() };
        var selectedKeys = Array.Empty<string>();
        var calculator = new ModulePricingCalculator();

        var act = () => calculator.Calculate(catalog, selectedKeys, "51-200");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*At least one module*");
    }
}
