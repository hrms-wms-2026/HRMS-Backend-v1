using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Mappers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestMapperTests
{
    [Fact]
    public void ToBalanceImpact_ReservesPaidHoursOnly()
    {
        var response = LeaveRequestMapper.ToBalanceImpact(
            currentRemainingHours: 4m,
            currentPendingHours: 1m,
            paidHours: 2.5m);

        response.CurrentRemainingHours.Should().Be(4m);
        response.PendingAfterSubmitHours.Should().Be(3.5m);
        response.RemainingAfterSubmitHours.Should().Be(1.5m);
    }
}
