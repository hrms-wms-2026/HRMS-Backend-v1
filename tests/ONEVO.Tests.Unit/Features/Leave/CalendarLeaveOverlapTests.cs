using FluentAssertions;
using ONEVO.Infrastructure.Services.Leave;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave;

public class CalendarLeaveOverlapTests
{
    [Fact]
    public void Overlaps_WhenEventCoversLeaveWindow()
    {
        CalendarLeaveOverlap.Overlaps(
            new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 12)).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_WhenEventIsOutsideWindow_IsFalse()
    {
        CalendarLeaveOverlap.Overlaps(
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 12)).Should().BeFalse();
    }
}
