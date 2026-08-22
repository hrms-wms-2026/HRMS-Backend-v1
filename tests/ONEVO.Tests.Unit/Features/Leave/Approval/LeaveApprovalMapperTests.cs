using FluentAssertions;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalMapperTests
{
    [Fact]
    public void CalculateRemaining_AfterApproval_KeepsPendingReservedBalanceStable()
    {
        var remaining = LeaveApprovalMapper.CalculateRemaining(20m, 0m, 8m, 0m);
        remaining.Should().Be(12m);
    }
}

public class LeaveApprovalVocabularyTests
{
    [Fact]
    public void InformationRequestedStatus_UsesStableWireValue()
    {
        LeaveRequestStatuses.InformationRequested.Should().Be("information_requested");
        LeaveRequestApproverStatuses.InformationRequested.Should().Be("information_requested");
    }
}

public class LeaveApprovalOptionsTests
{
    [Fact]
    public void SectionName_IsLeaveApprovals()
    {
        ONEVO.Application.Features.Leave.Approval.Options.LeaveApprovalOptions.SectionName.Should().Be("Leave:Approvals");
    }

    [Fact]
    public void AllowSelfApproval_DefaultsToFalse()
    {
        new ONEVO.Application.Features.Leave.Approval.Options.LeaveApprovalOptions().AllowSelfApproval.Should().BeFalse();
    }
}
