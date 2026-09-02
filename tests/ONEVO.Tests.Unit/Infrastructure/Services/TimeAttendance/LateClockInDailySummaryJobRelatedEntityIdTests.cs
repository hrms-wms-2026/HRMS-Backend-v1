using Xunit;
using ONEVO.Infrastructure.Services.TimeAttendance;

namespace ONEVO.Tests.Unit.Infrastructure.Services.TimeAttendance;

public sealed class LateClockInDailySummaryJobRelatedEntityIdTests
{
    [Fact]
    public void BuildRelatedEntityId_IsDeterministic_ForSameInputs()
    {
        var legalEntityId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 1);

        var first = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, date);
        var second = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, date);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildRelatedEntityId_Differs_AcrossDates()
    {
        var legalEntityId = Guid.NewGuid();

        var day1 = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, new DateOnly(2026, 9, 1));
        var day2 = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, new DateOnly(2026, 9, 2));

        Assert.NotEqual(day1, day2);
    }

    [Fact]
    public void BuildRelatedEntityId_Differs_AcrossLegalEntities()
    {
        var date = new DateOnly(2026, 9, 1);

        var a = LateClockInDailySummaryJob.BuildRelatedEntityId(Guid.NewGuid(), date);
        var b = LateClockInDailySummaryJob.BuildRelatedEntityId(Guid.NewGuid(), date);

        Assert.NotEqual(a, b);
    }
}
