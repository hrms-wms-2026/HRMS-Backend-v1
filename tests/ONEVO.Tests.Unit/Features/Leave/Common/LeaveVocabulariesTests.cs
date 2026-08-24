using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Common;

public class LeaveVocabulariesTests
{
    [Fact]
    public void LeaveTypeCategories_HasAllSevenSpecValues()
    {
        Assert.Equal("annual", LeaveTypeCategories.Annual);
        Assert.Equal("sick", LeaveTypeCategories.Sick);
        Assert.Equal("maternity", LeaveTypeCategories.Maternity);
        Assert.Equal("paternity", LeaveTypeCategories.Paternity);
        Assert.Equal("compassionate", LeaveTypeCategories.Compassionate);
        Assert.Equal("unpaid", LeaveTypeCategories.Unpaid);
        Assert.Equal("custom", LeaveTypeCategories.Custom);
    }

    [Fact]
    public void LeaveGenderRestrictions_DefaultIsAll()
    {
        Assert.Equal("all", LeaveGenderRestrictions.All);
    }

    [Fact]
    public void LeaveHalfDayPeriods_HasNoneAmPm()
    {
        Assert.Equal("am", LeaveHalfDayPeriods.Am);
        Assert.Equal("pm", LeaveHalfDayPeriods.Pm);
        Assert.Null(LeaveHalfDayPeriods.None);
    }
}
