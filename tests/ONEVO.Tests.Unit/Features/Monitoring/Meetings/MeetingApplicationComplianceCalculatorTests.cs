using FluentAssertions;
using ONEVO.Application.Features.Monitoring.Meetings.Services;

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public sealed class MeetingApplicationComplianceCalculatorTests
{
    [Fact]
    public void Brief_open_does_not_count_as_participation()
    {
        var result = MeetingApplicationComplianceCalculator.Calculate(durationSeconds: 3600, matchingSamples: 1);

        result.IsCompliant.Should().BeFalse();
        result.UsagePercentage.Should().BeApproximately(1.67m, 0.01m);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    public void One_hour_meeting_requires_at_least_half_the_duration(int samples, bool expected)
    {
        var result = MeetingApplicationComplianceCalculator.Calculate(durationSeconds: 3600, matchingSamples: samples);

        result.RequiredSeconds.Should().Be(1800);
        result.IsCompliant.Should().Be(expected);
    }

    [Fact]
    public void Usage_is_capped_at_meeting_duration()
    {
        var result = MeetingApplicationComplianceCalculator.Calculate(durationSeconds: 900, matchingSamples: 100);

        result.UsedSeconds.Should().Be(900);
        result.UsagePercentage.Should().Be(100m);
    }
}
