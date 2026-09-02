using FluentValidation.TestHelper;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommandValidatorTests
{
    private readonly EditTaskCommandValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ProgressPercent_OutOfRange_Fails(int percent)
    {
        var command = new EditTaskCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, percent, null);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProgressPercent);
    }

    [Fact]
    public void Reason_TooLong_Fails()
    {
        var command = new EditTaskCommand(
            Guid.NewGuid(), "Title", null, "medium", null, null, null, null, new string('a', 1001));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
