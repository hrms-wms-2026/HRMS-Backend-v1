using FluentAssertions;
using Microsoft.Extensions.Options;
using ONEVO.Infrastructure.ExternalServices.Email;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Email;

/// <summary>
/// <c>Email:MaxPerSecond</c> is validated at startup. A negative limit is rejected rather than clamped:
/// a typo in the configuration must not silently turn the pacing off, because "unlimited" is the opposite
/// of what a negative number was meant to say.
/// </summary>
public sealed class EmailRateOptionsValidatorTests
{
    private static readonly EmailRateOptionsValidator Validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(300)]
    public void AValidLimit_Passes(int maxPerSecond)
    {
        var result = Validator.Validate(null, new EmailRateOptions { MaxPerSecond = maxPerSecond });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ANegativeLimit_Fails_AndNamesTheKey()
    {
        var result = Validator.Validate(null, new EmailRateOptions { MaxPerSecond = -1 });

        Assert.True(result.Failed);
        Assert.Contains("Email:MaxPerSecond", result.FailureMessage);
    }
}
