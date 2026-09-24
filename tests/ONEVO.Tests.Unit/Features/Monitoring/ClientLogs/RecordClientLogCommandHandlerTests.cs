using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

namespace ONEVO.Tests.Unit.Features.Monitoring.ClientLogs;

public class RecordClientLogCommandHandlerTests
{
    // NullLogger, not a mock: verifying the exact generic Log<TState>() call signature that
    // ILogger's LogError/LogWarning extension methods produce is brittle with NSubstitute (the
    // TState type is an internal framework type you can't name from a test). This codebase has
    // no existing precedent for asserting ILogger call content either - every other test that
    // touches ILogger (e.g. CsrfProtectionMiddlewareTests) only injects it as a stub dependency.
    // The observable contract worth testing here is the returned Result, not the log call shape.
    private readonly RecordClientLogCommandHandler _handler =
        new(NullLogger<RecordClientLogCommandHandler>.Instance);

    private static RecordClientLogCommand Command(string level = "error") => new(
        AdminUserId: Guid.NewGuid().ToString(),
        AdminEmail: "admin@example.com",
        Level: level,
        Message: "Something broke",
        Context: new Dictionary<string, object?> { ["route"] = "/tenants" },
        ClientTimestamp: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_ErrorLevel_ReturnsSuccess()
    {
        var result = await _handler.Handle(Command(level: "error"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WarnLevel_ReturnsSuccess()
    {
        var result = await _handler.Handle(Command(level: "warn"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NullContext_ReturnsSuccess()
    {
        var command = Command() with { Context = null };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
