using FluentValidation.TestHelper;
using ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

namespace ONEVO.Tests.Unit.Features.Monitoring.ClientLogs;

public class RecordClientLogCommandValidatorTests
{
    private readonly RecordClientLogCommandValidator _sut = new();

    private static RecordClientLogCommand ValidCommand(string level = "error", string message = "Something broke") =>
        new(
            AdminUserId: Guid.NewGuid().ToString(),
            AdminEmail: "admin@example.com",
            Level: level,
            Message: message,
            Context: null,
            ClientTimestamp: DateTimeOffset.UtcNow);

    [Fact]
    public void Valid_command_passes()
    {
        var result = _sut.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_level_fails()
    {
        var result = _sut.TestValidate(ValidCommand(level: ""));
        result.ShouldHaveValidationErrorFor(x => x.Level);
    }

    [Fact]
    public void Empty_message_fails()
    {
        var result = _sut.TestValidate(ValidCommand(message: ""));
        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Message_over_4000_chars_fails()
    {
        var result = _sut.TestValidate(ValidCommand(message: new string('x', 4001)));
        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Message_at_exactly_4000_chars_passes()
    {
        var result = _sut.TestValidate(ValidCommand(message: new string('x', 4000)));
        result.ShouldNotHaveValidationErrorFor(x => x.Message);
    }
}
