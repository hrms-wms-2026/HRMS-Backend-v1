using FluentValidation.TestHelper;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class PushTaskCommandValidatorTests
{
    private readonly PushTaskCommandValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Percent_OutOfRange_Fails(int percent)
    {
        var result = _validator.TestValidate(new PushTaskCommand(Guid.NewGuid(), percent, null));

        result.ShouldHaveValidationErrorFor(x => x.Percent);
    }

    [Fact]
    public void Reason_TooLong_Fails()
    {
        var result = _validator.TestValidate(new PushTaskCommand(Guid.NewGuid(), 50, new string('a', 1001)));

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
