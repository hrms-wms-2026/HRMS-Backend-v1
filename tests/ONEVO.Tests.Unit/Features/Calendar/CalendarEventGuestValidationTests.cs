using ONEVO.Application.Features.Calendar.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarEventGuestValidationTests
{
    [Theory]
    [InlineData("guest@example.com", "guest@example.com")]
    [InlineData("  Guest@Example.COM  ", "guest@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk", "first.last+tag@sub.example.co.uk")]
    public void TryNormalizeGuestEmail_ValidAddress_IsTrimmedAndLowerCased(string raw, string expected)
    {
        Assert.True(CalendarEventValidation.TryNormalizeGuestEmail(raw, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    [InlineData("Ada Lovelace <ada@example.com>")]
    [InlineData("two@@example.com")]
    [InlineData("javascript:alert(1)")]
    public void TryNormalizeGuestEmail_InvalidAddress_IsRejected(string? raw)
    {
        Assert.False(CalendarEventValidation.TryNormalizeGuestEmail(raw, out var email));
        Assert.Equal(string.Empty, email);
    }

    [Fact]
    public void TryNormalizeGuestEmail_LongerThan255Characters_IsRejected()
    {
        var tooLong = new string('a', 250) + "@example.com";
        Assert.False(CalendarEventValidation.TryNormalizeGuestEmail(tooLong, out _));
    }

    [Fact]
    public void NormalizeGuestEmails_DeduplicatesCaseInsensitively_PreservingOrder()
    {
        var (emails, invalid) = CalendarEventValidation.NormalizeGuestEmails(["B@x.co", "a@x.co", "b@X.co"]);

        Assert.Null(invalid);
        Assert.Equal(["b@x.co", "a@x.co"], emails);
    }

    [Fact]
    public void NormalizeGuestEmails_ReturnsTheFirstInvalidEntry()
    {
        var (emails, invalid) = CalendarEventValidation.NormalizeGuestEmails(["ok@x.co", "bad", "worse"]);

        Assert.Empty(emails);
        Assert.Equal("bad", invalid);
    }

    [Fact]
    public void NormalizeGuestEmails_NullInput_IsEmpty()
    {
        var (emails, invalid) = CalendarEventValidation.NormalizeGuestEmails(null);

        Assert.Empty(emails);
        Assert.Null(invalid);
    }
}
