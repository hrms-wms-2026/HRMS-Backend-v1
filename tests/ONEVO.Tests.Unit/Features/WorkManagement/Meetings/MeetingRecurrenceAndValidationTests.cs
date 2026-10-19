using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Recurrence expands in the series' own timezone (spec 5.2), and create input is validated with the §7 codes.</summary>
public sealed class MeetingRecurrenceAndValidationTests
{
    private static readonly IcalNetMeetingRecurrence Recurrence = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Weekly_StaysAtNineLocal_AcrossTheLondonDstChange()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var nineLocal = (int day) => new DateTimeOffset(new DateTime(2026, 10, day, 9, 0, 0),
            london.GetUtcOffset(new DateTime(2026, 10, day, 9, 0, 0))).ToUniversalTime();
        var dtStart = nineLocal(19);   // BST: 08:00Z

        var occurrences = Recurrence.Occurrences("FREQ=WEEKLY;COUNT=3", dtStart, "Europe/London", dtStart, dtStart.AddDays(30));

        // Oct 19 08:00Z (BST), Oct 26 09:00Z (GMT after Oct 25), Nov 2 09:00Z - always 09:00 in London.
        Assert.Equal([nineLocal(19), nineLocal(26), new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)], occurrences);
        Assert.All(occurrences, o => Assert.Equal(9, TimeZoneInfo.ConvertTime(o, london).Hour));
    }

    [Fact]
    public void OneOff_ReturnsItsStartOnlyInsideTheRange()
    {
        var start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(5.5));

        Assert.Equal([start.ToUniversalTime()], Recurrence.Occurrences(null, start, "Asia/Colombo", start.AddDays(-1), start.AddDays(1)));
        Assert.Empty(Recurrence.Occurrences(null, start, "Asia/Colombo", start.AddDays(1), start.AddDays(2)));
    }

    [Theory]
    [InlineData("FREQ=MINUTELY")]          // > 500 a year
    [InlineData("FREQ=HOURLY;INTERVAL=2")]
    [InlineData("NOT A RULE")]
    [InlineData("FREQ=WEEKLY;COUNT=501")]
    public void DenseOrBrokenRules_AreInvalid(string rule)
        => AssertCode(Recurrence.Validate(rule, Now.AddDays(1), "UTC"), MeetingErrorCodes.RecurrenceInvalid);

    [Fact]
    public void AnOpenEndedWeeklyRule_IsValid()
        => Assert.True(Recurrence.Validate("FREQ=WEEKLY;BYDAY=MO", Now.AddDays(1), "Asia/Colombo").IsSuccess);

    [Fact]
    public void UnknownTimezone_IsInvalid()
        => AssertCode(Recurrence.Validate(null, Now.AddDays(1), "Mars/Olympus"), MeetingErrorCodes.InvalidTimezone);

    private static CreateMeetingRequest Request(DateTimeOffset? start = null, int duration = 30, string title = "Standup",
        string timezone = "UTC", string? rule = null, IReadOnlyList<string>? guests = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), title, null, null, false, start ?? Now.AddDays(1), duration, false, timezone, rule,
            [], guests ?? [], null);

    [Fact]
    public void Validator_ReportsEachCode()
    {
        var validator = new MeetingCreateValidator(Recurrence);

        Assert.True(validator.Validate(Request(), Now).IsSuccess);
        AssertCode(validator.Validate(Request(duration: 0), Now), MeetingErrorCodes.InvalidTime);
        AssertCode(validator.Validate(Request(start: Now.AddMinutes(-1)), Now), MeetingErrorCodes.StartInPast);
        AssertCode(validator.Validate(Request(timezone: "Nope/Nowhere"), Now), MeetingErrorCodes.InvalidTimezone);
        AssertCode(validator.Validate(Request(rule: "FREQ=MINUTELY"), Now), MeetingErrorCodes.RecurrenceInvalid);
        AssertCode(validator.Validate(Request(title: "  "), Now), MeetingErrorCodes.TitleInvalid);
        AssertCode(validator.Validate(Request(guests: ["not-an-email"]), Now), MeetingErrorCodes.GuestEmailInvalid);
    }

    [Fact]
    public void GuestEmails_AreDeduplicatedCaseInsensitively()
        => Assert.Equal(["Client@Example.com", "vendor@example.com"],
            MeetingCreateValidator.NormalizeGuests([" Client@Example.com", "client@example.com", "vendor@example.com "]));

    private static void AssertCode(ONEVO.Application.Common.Models.Result result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
    }
}
