using FluentAssertions;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationVocabularyTests
{
    [Fact]
    public void CancellationStatuses_UseStableWireValues()
    {
        LeaveRequestApproverStatuses.Cancelled.Should().Be("cancelled");
        LeaveRequestDayAllocationStatuses.Active.Should().Be("active");
        LeaveRequestDayAllocationStatuses.Cancelled.Should().Be("cancelled");
    }
}
