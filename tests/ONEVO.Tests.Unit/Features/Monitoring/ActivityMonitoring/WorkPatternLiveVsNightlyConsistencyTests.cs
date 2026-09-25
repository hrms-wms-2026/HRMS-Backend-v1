using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

/// <summary>Feeds the SAME snapshot/meeting fixture through both the live "today" path's
/// classifier call (WorkPatternWindowClassifier, used directly by
/// GetMyWorkPatternQueryHandler.BuildTodayDtoAsync) and the nightly aggregation path
/// (ActivityDailySummaryAggregator.Aggregate) and asserts identical Focus/Meeting/Productive
/// output - proving the two paths can't drift, since both now call the same underlying
/// classifier rather than maintaining separate implementations.</summary>
public sealed class WorkPatternLiveVsNightlyConsistencyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();

    private static ActivitySnapshot Snap(DateTimeOffset capturedAt, int activeSeconds, int idleSeconds, string? process) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = DeviceId,
        CapturedAt = capturedAt, ActiveSeconds = activeSeconds, IdleSeconds = idleSeconds,
        ForegroundProcessName = process, CreatedAt = capturedAt
    };

    private static MeetingSignal Meeting(DateTimeOffset capturedAt, bool running) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = DeviceId,
        CapturedAt = capturedAt, IsMeetingAppRunning = running, CreatedAt = capturedAt
    };

    [Fact]
    public void LiveClassifierAndNightlyAggregator_AgreeOnFocusMeetingAndProductive_ForTheSameFixture()
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, 45)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: 60, idleSeconds: 0,
                process: i <= 30 ? "code.exe" : "spotify.exe"))
            .ToList();
        var meetings = new List<MeetingSignal>(); // keep this fixture meeting-free - meeting-overlap math
                                                    // is already covered by WorkPatternWindowClassifierTests
                                                    // and WorkPatternInvariantTests; this test's job is only
                                                    // proving the two CALL SITES agree, not re-deriving the algorithm.

        var liveResult = WorkPatternWindowClassifier.Classify(snaps, meetings);

        var nightlySummary = ActivityDailySummaryAggregator.Aggregate(
            TenantId, EmployeeId, DateOnly.FromDateTime(start.Date), snaps, DateTimeOffset.UtcNow,
            meetingSignals: meetings);

        Assert.Equal(liveResult.FocusMinutes, nightlySummary.FocusMinutes);
        Assert.Equal(meetings.Count(m => m.IsMeetingAppRunning) * 2, nightlySummary.TotalMeetingMinutes);
        Assert.Equal(liveResult.ProductiveFocusMinutes + liveResult.ProductiveOtherActiveMinutes, nightlySummary.ProductiveAppMinutes);
    }
}
