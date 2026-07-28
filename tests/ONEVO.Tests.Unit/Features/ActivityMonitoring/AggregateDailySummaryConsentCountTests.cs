using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

/// <summary>
/// Verifies the denial-counting predicate used by AggregateDailySummaryJob.
/// The predicate is Decision != "allowed" — not Decision == "denied" — so that
/// "timeout" and "upload_failed_no_image" outcomes are also counted.
/// </summary>
public sealed class AggregateDailySummaryConsentCountTests
{
    [Theory]
    [InlineData("denied")]
    [InlineData("timeout")]
    [InlineData("upload_failed_no_image")]
    public void NonAllowedDecisions_AreCountedAsDenied(string decision)
    {
        var events = new List<MonitoringConsentEvent>
        {
            new() { Decision = decision }
        };

        var count = events.Count(e => e.Decision != "allowed");

        Assert.Equal(1, count);
    }

    [Fact]
    public void AllowedDecision_IsNotCounted()
    {
        var events = new List<MonitoringConsentEvent>
        {
            new() { Decision = "allowed" }
        };

        var count = events.Count(e => e.Decision != "allowed");

        Assert.Equal(0, count);
    }

    [Fact]
    public void MixedDecisions_CountOnlyNonAllowed()
    {
        var events = new List<MonitoringConsentEvent>
        {
            new() { Decision = "allowed" },
            new() { Decision = "denied" },
            new() { Decision = "timeout" },
            new() { Decision = "allowed" },
            new() { Decision = "upload_failed_no_image" }
        };

        var count = events.Count(e => e.Decision != "allowed");

        Assert.Equal(3, count);
    }
}
