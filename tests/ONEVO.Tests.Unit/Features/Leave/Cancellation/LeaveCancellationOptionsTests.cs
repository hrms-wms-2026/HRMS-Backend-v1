using FluentAssertions;
using ONEVO.Application.Features.Leave.Cancellation.Options;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationOptionsTests
{
    [Fact]
    public void SectionName_IsLeaveCancellation()
    {
        LeaveCancellationOptions.SectionName.Should().Be("Leave:Cancellation");
    }

    [Fact]
    public void FallbackTimezone_MustBeConfigured()
    {
        var options = new LeaveCancellationOptions();
        options.FallbackTimezone.Should().BeNull();
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Colombo")]
    [InlineData("Sri Lanka Standard Time")]
    public void ResolveTimezone_AcceptsIanaAndWindowsTimezoneIds(string value)
    {
        LeaveCancellationOptions.ResolveTimezone(value).Should().NotBeNull();
    }
}
