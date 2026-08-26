using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Mappers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestMapperTests
{
    [Fact]
    public void ToBalanceImpact_ReservesPaidDaysOnly()
    {
        var response = LeaveRequestMapper.ToBalanceImpact(
            currentRemainingDays: 4m,
            currentPendingDays: 1m,
            paidDays: 2.5m);

        response.CurrentRemainingDays.Should().Be(4m);
        response.PendingAfterSubmitDays.Should().Be(3.5m);
        response.RemainingAfterSubmitDays.Should().Be(1.5m);
    }
}
