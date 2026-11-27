using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Behaviors;

public class PerformanceBehaviorTests
{
    private readonly Mock<ILogger<PerformanceBehavior<TestRequest, string>>> _logger = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    public record TestRequest : IRequest<string>;

    private PerformanceBehavior<TestRequest, string> BuildBehavior(int thresholdMs) =>
        new(_logger.Object, _currentUser.Object,
            Options.Create(new PerformanceOptions { SlowRequestThresholdMs = thresholdMs }));

    [Fact]
    public void Default_threshold_is_the_200_ms_team_target()
    {
        new PerformanceOptions().SlowRequestThresholdMs.Should().Be(200);
        PerformanceOptions.SectionName.Should().Be("Performance");
    }

    [Fact]
    public async Task Handle_slower_than_threshold_logs_a_warning()
    {
        var behavior = BuildBehavior(thresholdMs: 10);
        RequestHandlerDelegate<string> next = async _ =>
        {
            await Task.Delay(60);
            return "ok";
        };

        var result = await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        VerifyWarnings(Times.Once());
    }

    [Fact]
    public async Task Handle_faster_than_threshold_logs_nothing()
    {
        var behavior = BuildBehavior(thresholdMs: 10_000);
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        VerifyWarnings(Times.Never());
    }

    private void VerifyWarnings(Times times) =>
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
