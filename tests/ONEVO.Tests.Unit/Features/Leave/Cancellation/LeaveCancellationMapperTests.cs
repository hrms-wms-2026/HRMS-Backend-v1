using FluentAssertions;
using ONEVO.Application.Features.Leave.Cancellation.Mappers;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationMapperTests
{
    [Fact]
    public void ToResponse_MapsPartialCancellationFields()
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            Status = LeaveRequestStatuses.Cancelled,
            CancellationReason = "coverage",
            PartialCancelEffectiveDate = new DateOnly(2026, 8, 24)
        };
        var at = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

        var response = LeaveCancellationMapper.ToResponse(request, true, 0m, 1.5m, 10m, at);

        response.IsPartialCancellation.Should().BeTrue();
        response.RestoredUsedDays.Should().Be(1.5m);
        response.EffectiveDate.Should().Be(new DateOnly(2026, 8, 24));
        response.Reason.Should().Be("coverage");
    }
}
