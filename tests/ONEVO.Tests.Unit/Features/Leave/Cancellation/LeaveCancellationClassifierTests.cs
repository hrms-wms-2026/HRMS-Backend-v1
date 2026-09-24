using FluentAssertions;
using ONEVO.Application.Features.Leave.Cancellation.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationClassifierTests
{
    private readonly LeaveCancellationClassifier _sut = new();

    [Fact]
    public void Classify_AlreadyCancelled_ReturnsProductMessage()
    {
        var result = _sut.Classify(LeaveRequestStatuses.Cancelled, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), new DateOnly(2026, 8, 22), null);
        result.StatusCode.Should().Be(409);
        result.Error.Should().Be(LeaveCancellationMessages.AlreadyCancelled);
    }

    [Fact]
    public void Classify_Rejected_ReturnsProductMessage()
    {
        var result = _sut.Classify(LeaveRequestStatuses.Rejected, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), new DateOnly(2026, 8, 22), null);
        result.Error.Should().Be(LeaveCancellationMessages.Rejected);
    }

    [Fact]
    public void Classify_FullyPassedPeriod_ReturnsProductMessage()
    {
        var result = _sut.Classify(LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 22), null);
        result.Error.Should().Be(LeaveCancellationMessages.PeriodPassed);
    }

    [Theory]
    [InlineData(LeaveRequestStatuses.Pending)]
    [InlineData(LeaveRequestStatuses.InformationRequested)]
    public void Classify_PendingStyleStatuses_ArePendingStyle(string status)
    {
        var result = _sut.Classify(status, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), new DateOnly(2026, 8, 22), null);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Kind.Should().Be(LeaveCancellationKind.PendingStyle);
    }

    [Fact]
    public void Classify_ApprovedFutureRequest_IsFullCancellation()
    {
        var result = _sut.Classify(LeaveRequestStatuses.Approved, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), new DateOnly(2026, 8, 22), null);
        result.Value!.Kind.Should().Be(LeaveCancellationKind.ApprovedFull);
        result.Value.EffectiveDate.Should().BeNull();
    }

    [Fact]
    public void Classify_ApprovedInProgress_DefaultsEffectiveDateToBusinessDate()
    {
        var result = _sut.Classify(LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 22), null);
        result.Value!.Kind.Should().Be(LeaveCancellationKind.ApprovedPartial);
        result.Value.EffectiveDate.Should().Be(new DateOnly(2026, 8, 22));
    }

    [Fact]
    public void Classify_ApprovedInProgress_AcceptsFutureEffectiveDateThroughEndDate()
    {
        var result = _sut.Classify(
            LeaveRequestStatuses.Approved,
            new DateOnly(2026, 8, 20),
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 8, 22),
            new DateOnly(2026, 8, 24));
        result.Value!.Kind.Should().Be(LeaveCancellationKind.ApprovedPartial);
        result.Value.EffectiveDate.Should().Be(new DateOnly(2026, 8, 24));
    }

    [Fact]
    public void Classify_EffectiveDateOutsideRange_Fails()
    {
        var result = _sut.Classify(
            LeaveRequestStatuses.Approved,
            new DateOnly(2026, 8, 20),
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 8, 22),
            new DateOnly(2026, 8, 30));
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(LeaveCancellationMessages.InvalidEffectiveDate);
    }
}
