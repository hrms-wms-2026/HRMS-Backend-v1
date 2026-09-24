using FluentValidation.TestHelper;
using ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class DeleteLegalEntityCommandValidatorTests
{
    private readonly DeleteLegalEntityCommandValidator _validator = new();

    [Fact]
    public void Valid_ConfirmName_HasNoErrors()
    {
        var result = _validator.TestValidate(new DeleteLegalEntityCommand(Guid.NewGuid(), "Acme Lanka"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_ConfirmName_HasError()
    {
        var result = _validator.TestValidate(new DeleteLegalEntityCommand(Guid.NewGuid(), ""));
        result.ShouldHaveValidationErrorFor(x => x.ConfirmName);
    }
}
