using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Options;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarOptionsTests
{
    [Fact]
    public void SectionName_IsLeaveCalendar()
    {
        LeaveCalendarOptions.SectionName.Should().Be("Leave:Calendar");
    }

    [Theory]
    [InlineData("#2563EB")]
    [InlineData("#16a34a")]
    public void IsValidHexColor_AcceptsSixDigitHexColors(string value)
    {
        LeaveCalendarOptions.IsValidHexColor(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    public void IsValidHexColor_RejectsInvalidValues(string value)
    {
        LeaveCalendarOptions.IsValidHexColor(value).Should().BeFalse();
    }
}
