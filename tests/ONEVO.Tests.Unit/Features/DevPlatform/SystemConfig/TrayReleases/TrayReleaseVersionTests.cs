using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.TrayReleases;

public sealed class TrayReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("10.0.0", true)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData("1.2.3-beta", false)]
    [InlineData("v1.2.3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsOnlyThreePartNumericVersions(string? input, bool expected)
    {
        Assert.Equal(expected, TrayReleaseVersion.TryParse(input, out _));
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0", 1)]   // numeric, not lexicographic
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("0.9.9", "1.0.0", -1)]
    public void Compare_OrdersNumerically(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(TrayReleaseVersion.Compare(a, b)));
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]    // current below minimum
    [InlineData("1.1.0", "1.1.0", false)]   // equal is fine
    [InlineData("2.0.0", "1.1.0", false)]
    [InlineData("1.0.0", null, false)]      // no minimum set
    [InlineData("1.0.0", "", false)]
    public void IsMandatory_TrueOnlyWhenCurrentIsBelowMinimum(string current, string? min, bool expected)
    {
        Assert.Equal(expected, TrayReleaseVersion.IsMandatory(current, min));
    }

    [Fact]
    public void IsMandatory_FalseWhenCurrentVersionIsUnparseable()
    {
        // A tray reporting garbage must not be hard-blocked.
        Assert.False(TrayReleaseVersion.IsMandatory("garbage", "1.0.0"));
    }
}
