using FluentAssertions;
using FluentValidation.TestHelper;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

namespace ONEVO.Tests.Unit.Features.Tenancy;

public class CreateTenantCommandValidatorTests
{
    private readonly CreateTenantCommandValidator _validator = new();

    private static CreateTenantCommand ValidCommand() => new(
        Name: "Acme Corp",
        Slug: "acme",
        IndustryProfile: "Technology",
        CompanySizeRange: "1-10",
        LegalEntityName: "Acme Corp Pte. Ltd.",
        RegistrationNumber: "REG001",
        Country: "LK",
        Timezone: "UTC",
        Currency: "LKR",
        Subscription: null!,
        OwnerInvite: null);

    [Theory]
    [InlineData("ab")]      // too short (2 chars)
    [InlineData("a")]       // too short (1 char)
    [InlineData("-abc")]    // starts with hyphen
    [InlineData("abc-")]    // ends with hyphen
    [InlineData("ABC")]     // uppercase
    [InlineData("ab_cd")]   // underscore not allowed
    public void Slug_InvalidValues_FailValidation(string slug)
    {
        var command = ValidCommand() with { Slug = slug };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("my-company")]
    [InlineData("a1b")]
    [InlineData("hello-world-123")]
    public void Slug_ValidValues_PassValidation(string slug)
    {
        var command = ValidCommand() with { Slug = slug };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Slug);
    }
}
