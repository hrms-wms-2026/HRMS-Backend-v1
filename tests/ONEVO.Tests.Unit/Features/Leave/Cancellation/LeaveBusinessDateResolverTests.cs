using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Application.Features.Leave.Cancellation.Options;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveBusinessDateResolverTests
{
    [Fact]
    public void Today_UsesLegalEntityTimezoneWhenPresent()
    {
        var resolver = Create(new DateTimeOffset(2026, 8, 22, 22, 30, 0, TimeSpan.Zero), "UTC");
        resolver.Today("Asia/Colombo").Should().Be(new DateOnly(2026, 8, 23));
    }

    [Fact]
    public void Today_UsesFallbackTimezoneWhenLegalEntityTimezoneMissing()
    {
        var resolver = Create(new DateTimeOffset(2026, 8, 22, 22, 30, 0, TimeSpan.Zero), "UTC");
        resolver.Today(null).Should().Be(new DateOnly(2026, 8, 22));
    }

    [Fact]
    public void Today_UtcInstantNearMidnight_CanProduceDifferentDates()
    {
        var instant = new DateTimeOffset(2026, 8, 22, 22, 30, 0, TimeSpan.Zero);
        var utc = Create(instant, "UTC");
        var colombo = Create(instant, "Asia/Colombo");

        utc.Today(null).Should().Be(new DateOnly(2026, 8, 22));
        colombo.Today(null).Should().Be(new DateOnly(2026, 8, 23));
    }

    private static LeaveBusinessDateResolver Create(DateTimeOffset utcNow, string fallback)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(utcNow);
        return new LeaveBusinessDateResolver(
            clock.Object,
            Options.Create(new LeaveCancellationOptions { FallbackTimezone = fallback }));
    }
}
