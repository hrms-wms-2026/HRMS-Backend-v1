using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class NoOpLeaveCalendarHolidayProviderTests
{
    [Fact]
    public async Task ListHolidaysAsync_ReturnsEmptyList()
    {
        var provider = new NoOpLeaveCalendarHolidayProvider();

        var result = await provider.ListHolidaysAsync(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
