using FluentValidation.TestHelper;
using ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class CreateLegalEntityCommandValidatorTests
{
    private readonly CreateLegalEntityCommandValidator _validator = new();

    private static CreateLegalEntityCommand ValidCommand() => new(
        "Acme Lanka", "ACME", "REG-001", "LKA", "LKR", null, null, null);

    [Fact]
    public void Valid_Command_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_Name_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Empty_CompanyCode_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { CompanyCode = "" });
        result.ShouldHaveValidationErrorFor(x => x.CompanyCode);
    }

    [Fact]
    public void Empty_RegistrationNumber_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { RegistrationNumber = "" });
        result.ShouldHaveValidationErrorFor(x => x.RegistrationNumber);
    }

    [Fact]
    public void Empty_CountryCode_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { CountryCode = "" });
        result.ShouldHaveValidationErrorFor(x => x.CountryCode);
    }

    [Fact]
    public void Empty_CurrencyCode_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { CurrencyCode = "" });
        result.ShouldHaveValidationErrorFor(x => x.CurrencyCode);
    }

    [Fact]
    public void EmptyGuid_ParentLegalEntityId_HasError()
    {
        var result = _validator.TestValidate(ValidCommand() with { ParentLegalEntityId = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.ParentLegalEntityId);
    }
}
