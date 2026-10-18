using ONEVO.Application.Features.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>An occurrence's first-materialization id is a function of (series, original start), so a virtual calendar
/// occurrence and the task it later becomes share one id (meetings-as-tasks Phase 2 spec 5.5).</summary>
public sealed class MeetingOccurrenceIdsTests
{
    private static readonly Guid Series = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheSameSeriesAndStart_GiveTheSameId()
        => Assert.Equal(MeetingOccurrenceIds.For(Series, Start), MeetingOccurrenceIds.For(Series, Start));

    [Fact]
    public void ADifferentStart_GivesADifferentId()
        => Assert.NotEqual(MeetingOccurrenceIds.For(Series, Start), MeetingOccurrenceIds.For(Series, Start.AddDays(7)));

    [Fact]
    public void ADifferentSeries_GivesADifferentId()
        => Assert.NotEqual(MeetingOccurrenceIds.For(Series, Start), MeetingOccurrenceIds.For(Guid.NewGuid(), Start));

    [Fact]
    public void TheSameInstantInAnotherOffset_GivesTheSameId()
        => Assert.Equal(MeetingOccurrenceIds.For(Series, Start), MeetingOccurrenceIds.For(Series, Start.ToOffset(TimeSpan.FromHours(5.5))));
}
