using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class HolidayCalendarSettingsTests
{
    [Fact]
    public void EffectiveCountryCode_UsesOverride_WhenSet()
    {
        var settings = new HolidayCalendarSettings { DefaultCountryCode = "IN", OverrideCountryCode = "US" };
        Assert.Equal("US", settings.EffectiveCountryCode);
    }

    [Fact]
    public void EffectiveCountryCode_FallsBackToDefault_WhenOverrideIsNull()
    {
        var settings = new HolidayCalendarSettings { DefaultCountryCode = "IN", OverrideCountryCode = null };
        Assert.Equal("IN", settings.EffectiveCountryCode);
    }
}
