using FluentValidation.TestHelper;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.CreateChecklistTemplate;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class CreateChecklistTemplateCommandValidatorTests
{
    private readonly CreateChecklistTemplateCommandValidator _validator = new();

    [Theory]
    [InlineData("onboarding")]
    [InlineData("offboarding")]
    public void Validate_KnownTemplateType_HasNoValidationErrorForTemplateType(string templateType)
    {
        var command = new CreateChecklistTemplateCommand("Name", templateType, Guid.NewGuid(), null, null, new List<CreateChecklistTemplateTaskInput>());
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.TemplateType);
    }

    [Fact]
    public void Validate_UnknownTemplateType_HasValidationError()
    {
        var command = new CreateChecklistTemplateCommand("Name", "exit_interview_only", Guid.NewGuid(), null, null, new List<CreateChecklistTemplateTaskInput>());
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.TemplateType);
    }

    [Fact]
    public void Validate_EmptyLegalEntityId_HasValidationError()
    {
        var command = new CreateChecklistTemplateCommand("Name", "onboarding", Guid.Empty, null, null, new List<CreateChecklistTemplateTaskInput>());
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.LegalEntityId);
    }
}
