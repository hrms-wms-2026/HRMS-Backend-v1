using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateInvoice;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class CreateInvoiceCommandValidatorTests
{
    private readonly CreateInvoiceCommandValidator _validator = new();

    private static CreateInvoiceCommand ValidCommand() => new(
        Guid.NewGuid(),
        null,
        "USD",
        100m,
        10m,
        5m,
        null,
        null,
        null,
        null,
        "draft");

    [Fact]
    public void Validate_ValidCommand_Passes() =>
        _validator.Validate(ValidCommand()).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_NegativeSubtotal_Fails() =>
        _validator.Validate(ValidCommand() with { SubtotalAmount = -1m }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NegativeTax_Fails() =>
        _validator.Validate(ValidCommand() with { TaxAmount = -1m }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_NegativeDiscount_Fails() =>
        _validator.Validate(ValidCommand() with { DiscountAmount = -1m }).IsValid.Should().BeFalse();

    [Fact]
    public void Validate_InvalidCurrencyLength_Fails() =>
        _validator.Validate(ValidCommand() with { Currency = "US" }).IsValid.Should().BeFalse();
}
