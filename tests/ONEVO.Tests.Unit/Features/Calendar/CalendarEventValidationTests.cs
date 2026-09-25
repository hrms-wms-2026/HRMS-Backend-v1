using ONEVO.Application.Features.Calendar.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarEventValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidMeetingLink_NullOrBlank_IsValid_BecauseTheFieldIsOptional(string? link)
    {
        Assert.True(CalendarEventValidation.IsValidMeetingLink(link));
    }

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/abc")]
    [InlineData("http://zoom.us/j/123456789")]
    public void IsValidMeetingLink_AbsoluteHttpOrHttps_IsValid(string link)
    {
        Assert.True(CalendarEventValidation.IsValidMeetingLink(link));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("meet.google.com/abc-defg-hij")] // missing scheme
    [InlineData("ftp://files.example.com/meeting")]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void IsValidMeetingLink_NotAnAbsoluteHttpUrl_IsInvalid(string link)
    {
        Assert.False(CalendarEventValidation.IsValidMeetingLink(link));
    }
}
