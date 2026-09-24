using FluentValidation.TestHelper;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskEditRequest;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class RejectTaskEditRequestCommandValidatorTests
{
    private readonly RejectTaskEditRequestCommandValidator _validator = new();

    [Fact]
    public void RequestId_Empty_FailsValidation()
    {
        var result = _validator.TestValidate(
            new RejectTaskEditRequestCommand(Guid.Empty, "Out of scope"));

        result.ShouldHaveValidationErrorFor(x => x.RequestId);
    }

    [Fact]
    public void Comment_WhitespaceOnly_FailsValidationWithDecisionMessage()
    {
        var result = _validator.TestValidate(
            new RejectTaskEditRequestCommand(Guid.NewGuid(), "   "));

        result.ShouldHaveValidationErrorFor(x => x.Comment)
            .WithErrorMessage("A decision comment is required when rejecting.");
    }
}
