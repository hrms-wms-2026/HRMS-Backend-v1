using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Common;

public class LeaveVocabulariesTests
{
    [Fact]
    public void LeaveGenderRestrictions_DefaultIsAll()
    {
        Assert.Equal("all", LeaveGenderRestrictions.All);
    }

    [Fact]
    public void LeaveApprovalModes_IncludeNone()
    {
        Assert.Equal("none", LeaveApprovalModes.None);
        Assert.Equal(["any_one", "all_must_approve", "in_order", "none"], LeaveApprovalModes.All);
    }

    [Fact]
    public void LeaveAmountPeriods_AreMonthAndYear()
    {
        Assert.Equal(["month", "year"], LeaveAmountPeriods.All);
    }

    [Fact]
    public void LeaveEligibility_HasThreeOptions()
    {
        Assert.Equal(["immediately", "after_probation", "after_months"], LeaveEligibility.All);
    }

    [Fact]
    public void LeavePolicyTargetKinds_AreTheFourLevels()
    {
        Assert.Equal(["legal_entity", "department", "position", "employee"], LeavePolicyTargetKinds.All);
    }
}
