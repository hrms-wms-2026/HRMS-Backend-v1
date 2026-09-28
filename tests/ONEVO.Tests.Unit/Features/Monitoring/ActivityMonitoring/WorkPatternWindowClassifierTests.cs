using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class WorkPatternWindowClassifierTests
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
    public void Classify_NoSnapshots_ReturnsAllZero()
    {
        var result = WorkPatternWindowClassifier.Classify([], []);

        Assert.Equal(0, result.FocusMinutes);
        Assert.Equal(0, result.OtherActiveMinutes);
        Assert.Equal(0, result.IdleMinutes);
        Assert.Equal(0, result.MeetingBarMinutes);
        Assert.Equal(0, result.ProductiveFocusMinutes);
        Assert.Equal(0, result.ProductiveOtherActiveMinutes);
    }

    [Fact]
    public void Classify_PartialActiveIdleSnapshot_AllocatesSecondsNotWholeWindow()
    {
        // A single 60s window with 40s active + 20s idle must split 40/20, never count
        // the full 60s toward either bucket - this is the bug the model was corrected for.
        var t = new DateTimeOffset(2026, 9, 25, 9, 1, 0, TimeSpan.Zero);
        var snaps = new[] { Snap(t, activeSeconds: 40, idleSeconds: 20, process: "excel.exe") };

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        // 40s active, alone, is not a 30-min focus streak -> OtherActive.
        Assert.Equal(0, result.FocusMinutes);
        Assert.Equal(0, result.OtherActiveMinutes); // 40s < 60s = 0 whole minutes (floor)
        Assert.Equal(0, result.IdleMinutes);        // 20s < 60s = 0 whole minutes (floor)
    }

    [Fact]
    public void Classify_ThirtyMinuteSameProcessStreak_IsFocus()
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, 30)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: 60, idleSeconds: 0, process: "code.exe"))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        Assert.Equal(30, result.FocusMinutes);
        Assert.Equal(0, result.OtherActiveMinutes);
    }

    [Fact]
    public void Classify_TwentyNineMinuteStreak_DoesNotReachFocusThreshold()
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, 29)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: 60, idleSeconds: 0, process: "code.exe"))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        Assert.Equal(0, result.FocusMinutes);
        Assert.Equal(29, result.OtherActiveMinutes);
    }

    [Fact]
    public void Classify_MeetingOverlappingWindow_ExcludesWholeWindowFromEverythingElse()
    {
        // A meeting signal at 09:10 covers [09:08,09:10). A snapshot captured at 09:09
        // (window [09:08,09:09), 60s, half active) overlaps it - the WHOLE 60s (both the
        // active and idle portions) must go to MeetingBar, not split into Idle/OtherActive.
        var meetingAt = new DateTimeOffset(2026, 9, 25, 9, 10, 0, TimeSpan.Zero);
        var snapAt = new DateTimeOffset(2026, 9, 25, 9, 9, 0, TimeSpan.Zero); // window [09:08,09:09)
        var snaps = new[] { Snap(snapAt, activeSeconds: 40, idleSeconds: 20, process: "teams.exe") };
        var meetings = new[] { Meeting(meetingAt, running: true) };

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);

        Assert.Equal(1, result.MeetingBarMinutes); // whole 60s window -> MeetingBar, not split
        Assert.Equal(0, result.FocusMinutes);
        Assert.Equal(0, result.OtherActiveMinutes);
        Assert.Equal(0, result.IdleMinutes);
    }

    [Fact]
    public void Classify_MeetingOverlappingFullyIdleWindow_StillCountsWholeWindowAsMeeting()
    {
        // Passive meeting-listening: window is fully idle (0 active) but overlaps a meeting -
        // must still go entirely to MeetingBar, not to Idle. This is the exact bug this model
        // was redesigned to fix (a listening-only meeting minute must not read as "Idle").
        var meetingAt = new DateTimeOffset(2026, 9, 25, 9, 10, 0, TimeSpan.Zero);
        var snapAt = new DateTimeOffset(2026, 9, 25, 9, 9, 0, TimeSpan.Zero);
        var snaps = new[] { Snap(snapAt, activeSeconds: 0, idleSeconds: 60, process: "teams.exe") };
        var meetings = new[] { Meeting(meetingAt, running: true) };

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);

        Assert.Equal(1, result.MeetingBarMinutes);
        Assert.Equal(0, result.IdleMinutes);
    }

    [Fact]
    public void Classify_ProductiveAppOtherActive_CountsTowardProductiveOtherActive()
    {
        var t = new DateTimeOffset(2026, 9, 25, 9, 1, 0, TimeSpan.Zero);
        var snaps = new[] { Snap(t, activeSeconds: 60, idleSeconds: 0, process: "excel.exe") };

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        Assert.Equal(1, result.OtherActiveMinutes);
        Assert.Equal(1, result.ProductiveOtherActiveMinutes);
    }

    [Fact]
    public void Classify_PersonalAppLongStreak_IsFocusButNotProductiveFocus()
    {
        // 30+ min in a Personal-classified app (discord.exe) is a real Focus streak
        // behaviorally, but must NOT count toward ProductiveFocus - Focus does not imply
        // Productive.
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, 30)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: 60, idleSeconds: 0, process: "discord.exe"))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        Assert.Equal(30, result.FocusMinutes);
        Assert.Equal(0, result.ProductiveFocusMinutes);
    }

    [Fact]
    public void Classify_UnknownAppOtherActive_DoesNotCountTowardProductive()
    {
        var t = new DateTimeOffset(2026, 9, 25, 9, 1, 0, TimeSpan.Zero);
        var snaps = new[] { Snap(t, activeSeconds: 60, idleSeconds: 0, process: "some-unlisted-tool.exe") };

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        Assert.Equal(1, result.OtherActiveMinutes);
        Assert.Equal(0, result.ProductiveOtherActiveMinutes); // unclassified, not "judged unproductive" - just not counted
    }

    [Fact]
    public void Classify_EightHourWorkedExample_MatchesPlanDocument()
    {
        // Mirrors the plan's worked example exactly - see "Worked 8-hour example" in the plan doc.
        var day = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = new List<ActivitySnapshot>();
        var meetings = new List<MeetingSignal>();
        var cursor = day;

        // 20 meeting-overlapping windows: 15 active, 5 idle (all inside a meeting window each).
        for (var i = 0; i < 20; i++)
        {
            cursor = cursor.AddMinutes(1);
            meetings.Add(Meeting(cursor, running: true)); // one meeting sample per minute keeps every window covered
            snaps.Add(Snap(cursor, activeSeconds: i < 15 ? 60 : 0, idleSeconds: i < 15 ? 0 : 60, process: "teams.exe"));
        }
        // 4 extra meeting samples with no overlapping snapshot at all (off-device).
        for (var i = 0; i < 4; i++)
        {
            cursor = cursor.AddMinutes(1);
            meetings.Add(Meeting(cursor, running: true));
        }
        // 130-minute Focus streak in a Productive app.
        for (var i = 0; i < 130; i++)
        {
            cursor = cursor.AddMinutes(1);
            snaps.Add(Snap(cursor, activeSeconds: 60, idleSeconds: 0, process: "code.exe"));
        }
        // 180 minutes of OtherActive: 60 Productive-classified, 120 Personal/Unknown - alternating
        // in short (1-2 min) bursts so no single same-process run reaches the 30-min Focus
        // threshold (a long continuous run here would itself become Focus, not OtherActive).
        for (var i = 0; i < 60; i++) // 60 cycles of 3 minutes = 180 minutes total
        {
            cursor = cursor.AddMinutes(1);
            snaps.Add(Snap(cursor, activeSeconds: 60, idleSeconds: 0, process: "outlook.exe")); // 60 min total, Productive
            cursor = cursor.AddMinutes(1);
            snaps.Add(Snap(cursor, activeSeconds: 60, idleSeconds: 0, process: "spotify.exe")); // 120 min total, Personal
            cursor = cursor.AddMinutes(1);
            snaps.Add(Snap(cursor, activeSeconds: 60, idleSeconds: 0, process: "spotify.exe"));
        }
        // 40 minutes fully idle, no meeting.
        for (var i = 0; i < 40; i++)
        {
            cursor = cursor.AddMinutes(1);
            snaps.Add(Snap(cursor, activeSeconds: 0, idleSeconds: 60, process: null));
        }

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);

        Assert.Equal(130, result.FocusMinutes);
        Assert.Equal(180, result.OtherActiveMinutes);
        Assert.Equal(40, result.IdleMinutes);
        Assert.Equal(20, result.MeetingBarMinutes);
        Assert.Equal(130, result.ProductiveFocusMinutes);
        Assert.Equal(60, result.ProductiveOtherActiveMinutes);

        // Invariant checks
        Assert.Equal(370, result.FocusMinutes + result.OtherActiveMinutes + result.IdleMinutes + result.MeetingBarMinutes); // Observed
    }
}
