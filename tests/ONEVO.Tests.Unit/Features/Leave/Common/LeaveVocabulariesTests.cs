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

    [Fact]
    public void LeaveAccrualMethods_HasSpecValues()
    {
        Assert.Equal("annual", LeaveAccrualMethods.Annual);
        Assert.Equal("monthly", LeaveAccrualMethods.Monthly);
        Assert.Equal("daily", LeaveAccrualMethods.Daily);
        Assert.Contains("annual", LeaveAccrualMethods.All);
        Assert.Contains("monthly", LeaveAccrualMethods.All);
        Assert.Contains("daily", LeaveAccrualMethods.All);
    }

    [Fact]
    public void LeavePolicyVocabularies_ExposeAllCollectionsForValidators()
    {
        Assert.Contains(LeaveApprovalModes.AnyOne, LeaveApprovalModes.All);
        Assert.Contains(LeaveAccrualStarts.Immediately, LeaveAccrualStarts.All);
        Assert.Contains(LeaveProrationMethods.CalendarDays, LeaveProrationMethods.All);
    }
}
