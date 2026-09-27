using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public class BreakAllowanceMonitorTests
{
    [Fact]
    public void Evaluate_UnderAllowance_AllowsAnotherBreak()
    {
        var snapshot = BreakAllowanceMonitor.Evaluate(60, completedMinutes: 20, usedMinutes: 20);

        snapshot.CanStartBreak.Should().BeTrue();
        snapshot.Exceeded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_AtAllowance_LocksAnotherBreakWithoutExceeding()
    {
        var snapshot = BreakAllowanceMonitor.Evaluate(60, completedMinutes: 60, usedMinutes: 60);

        snapshot.CanStartBreak.Should().BeFalse();
        snapshot.Exceeded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_PastAllowance_LocksAndExceeds()
    {
        var snapshot = BreakAllowanceMonitor.Evaluate(60, completedMinutes: 0, usedMinutes: 61);

        snapshot.CanStartBreak.Should().BeFalse();
        snapshot.Exceeded.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoAllowance_DoesNotLock()
    {
        var snapshot = BreakAllowanceMonitor.Evaluate(null, completedMinutes: 90, usedMinutes: 90);

        snapshot.CanStartBreak.Should().BeTrue();
        snapshot.Exceeded.Should().BeFalse();
    }
}
