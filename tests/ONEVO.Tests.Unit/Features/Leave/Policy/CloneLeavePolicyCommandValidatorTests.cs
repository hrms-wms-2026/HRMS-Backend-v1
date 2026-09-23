using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CloneLeavePolicyCommandValidatorTests
{
    private readonly CloneLeavePolicyCommandValidator _validator = new();

    [Fact]
    public void ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(new CloneLeavePolicyCommand(
            Guid.NewGuid(), "LK Policy Copy", "LK", [Guid.NewGuid()], new DateOnly(2026, 1, 1), false));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_HasError()
    {
        var result = _validator.TestValidate(new CloneLeavePolicyCommand(
            Guid.NewGuid(), "", "LK", [Guid.NewGuid()], new DateOnly(2026, 1, 1), false));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void EmptyCountry_HasError()
    {
        var result = _validator.TestValidate(new CloneLeavePolicyCommand(
            Guid.NewGuid(), "Copy", "", [Guid.NewGuid()], new DateOnly(2026, 1, 1), false));

        result.ShouldHaveValidationErrorFor(x => x.Country);
    }
}
