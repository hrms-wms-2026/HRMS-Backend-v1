using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Application.Common.Behaviors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Behaviors;

public class UnhandledExceptionBehaviorTests
{
    private readonly Mock<ILogger<UnhandledExceptionBehavior<TestRequest, string>>> _logger = new();

    public record TestRequest : IRequest<string>;

    private UnhandledExceptionBehavior<TestRequest, string> BuildBehavior() =>
        new(_logger.Object);

    [Fact]
    public async Task Handle_WhenValidationExceptionThrown_RethrowsWithoutLoggingError()
    {
        var behavior = BuildBehavior();
        var failures = new List<ValidationFailure> { new("Field", "Field is required") };
        RequestHandlerDelegate<string> next = _ => Task.FromException<string>(new ValidationException(failures));

        var act = async () => await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUnexpectedExceptionThrown_LogsErrorAndRethrows()
    {
        var behavior = BuildBehavior();
        var expectedEx = new InvalidOperationException("Unexpected error");
        RequestHandlerDelegate<string> next = _ => Task.FromException<string>(expectedEx);

        var act = async () => await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                expectedEx,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
