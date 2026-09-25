# Work Pattern / Productivity Metric Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Employee Dashboard's Work Pattern card's broken "Productive = Focus+Meeting+Admin" formula (which mathematically collapses to just "Active" and never checks app-category) with a real, interval-safe, no-double-count Productive calculation built on already-shipped data (`AppCategoryClassifier`), fix the two hand-duplicated Focus-streak implementations, and correct several UI/labeling issues (misleading "Admin/Other" name, an unexplained blank distribution bar, an unlabeled Meeting-detection heuristic presented as fact).

**Architecture:** A single shared, pure, unit-testable classifier (`WorkPatternWindowClassifier`) replaces two duplicated Focus algorithms and becomes the one place that turns raw `ActivitySnapshot`/`MeetingSignal` rows into Focus/Meeting/OtherActive/Idle/Productive minutes. Both the live "today" query path and the nightly aggregation job call it, so live and historical numbers can never drift apart. The API adds one field (`ProductiveMinutes`) and renames one (`AdminMinutes` → `OtherActiveMinutes`); the frontend stops deriving Productive itself and consumes the real backend value.

**Tech Stack:** .NET 10 / EF Core (backend, `HRMS-Backend-v1`), Angular 18 signals (frontend, `Hrms--Web-application---front-end---v1`).

**Spec:** No separate spec file — the full frozen model was worked out interactively in this session's conversation (multiple rounds of review and correction) and is fully restated below; this plan is self-contained.

## Global Constraints

- **No mocks, no placeholders, no TODOs** — every task ships real, deployment-ready code (demo-critical release).
- `AppCategoryClassifier`'s hardcoded, non-tenant-configurable process lists (`src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/AppCategoryClassifier.cs`) are **out of scope to change** — consume `Classify(string? processName) → AppCategory{Productive,Personal,Unknown}` as-is.
- The separate Productivity Report feature (`GetProductivityReportQueryHandler.cs` and its `AppUsageSnapshot`-based pipeline) is **out of scope** — do not touch it.
- `ActivitySnapshot` window boundaries: `[CapturedAt − (ActiveSeconds+IdleSeconds), CapturedAt)` — confirmed from `ActivityCountCollector.cs:280-284` (tray app) and already used this way by `ActivityTimelineBuilder.cs:49-50`. `ActiveSeconds+IdleSeconds` always equals the snapshot's own capture interval (60-300s, config-driven) — this is a collector-level guarantee, not something to re-derive.
- **A single `ActivitySnapshot` CAN have both `ActiveSeconds > 0` AND `IdleSeconds > 0`** (confirmed: `ActivityCountCollector.cs:281-284`, e.g. `interval=60, secondsSinceInput=45` → `idleSeconds=45, activeSeconds=15`). All duration allocation must use `ActiveSeconds`/`IdleSeconds` specifically, never the full window width.
- `MeetingSignal` sample window: `[CapturedAt − 2min, CapturedAt)` (`MeetingDetector.cs` `SampleWindow = TimeSpan.FromMinutes(2)`, tray app — confirmed, do not change the tray app in this plan, backend-only work).
- Meeting detection is a Phase-1 heuristic (meeting-app process presence only) — every place it's labeled in UI/tooltips must say "(est.)" / "estimated," never imply verified attendance.
- "Meeting counts toward Productive" is an explicit **Phase-1 ONEXSO policy decision**, not an inferred fact — document it as such in code comments.
- "Unknown" app category does not count toward Productive, but must never be framed as "unproductive" in code comments/UI copy — it's unclassified data, not a negative judgment.

---

## The Frozen Metric Model

This is the complete, locked algorithm. Every task below implements a piece of exactly this — none of it is open for reinterpretation during implementation.

### Step 1 — classify every `ActivitySnapshot` window for the day, ordered by `CapturedAt`

```
for each ActivitySnapshot window w (window = [CapturedAt-(ActiveSeconds+IdleSeconds), CapturedAt)):

    if w overlaps ANY true MeetingSignal's window [CapturedAt-2min, CapturedAt):
        MeetingBarSeconds += (w.ActiveSeconds + w.IdleSeconds)   // WHOLE window - meeting-ness
                                                                   // doesn't depend on keyboard/mouse activity
        continue   // this window contributes to NOTHING else below - excluded entirely

    IdleSecondsTotal += w.IdleSeconds

    if w.ActiveSeconds > 0:
        category = AppCategoryClassifier.Classify(w.ForegroundProcessName)   // Productive | Personal | Unknown

        if w is inside a Focus streak (30+ min contiguous same ForegroundProcessName,
                                        among windows that reached this point, i.e. non-meeting-overlapping):
            FocusSecondsTotal += w.ActiveSeconds
            if category == Productive:
                ProductiveFocusSeconds += w.ActiveSeconds
        else:
            OtherActiveSecondsTotal += w.ActiveSeconds
            if category == Productive:
                ProductiveOtherActiveSeconds += w.ActiveSeconds
```

### Step 2 — independent headline Meeting measurement (unchanged from today's formula)

```
MeetingHeadlineMinutes = count(MeetingSignal rows for the day where IsMeetingAppRunning == true) × 2
```
This is **intentionally separate** from `MeetingBarSeconds` above. It stays independent of whether any `ActivitySnapshot` exists at all, so genuinely off-device meeting time (phone call, no PC activity, tray produced zero snapshots for that stretch) still counts as a meeting — `MeetingBarSeconds` by construction cannot see time with no overlapping snapshot row.

### Step 3 — derive the final numbers (all in minutes = seconds/60, floor)

```
Focus         = FocusSecondsTotal / 60
OtherActive   = OtherActiveSecondsTotal / 60
Idle          = IdleSecondsTotal / 60
MeetingBar    = MeetingBarSeconds / 60          // internal only - NOT the headline Meeting card
Meeting       = MeetingHeadlineMinutes           // THE headline Meeting card value

Observed      = Focus + OtherActive + Idle + MeetingBar        // exact, by construction
EngagedTime   = Focus + OtherActive + MeetingBar                // = Observed - Idle

Productive    = MeetingBar + (ProductiveFocusSeconds/60) + (ProductiveOtherActiveSeconds/60)
ProductiveRate = EngagedTime > 0 ? Productive / EngagedTime : null   // "-" in UI when null

MeetingOffDeskMinutes = max(0, Meeting - MeetingBar)   // only shown/captioned when > 0
```

### Invariants (test these exactly, do not clamp to fake them)

```
Focus + OtherActive + Idle + MeetingBar = Observed          (exact)
Productive <= EngagedTime                                    (exact: each of Productive's 3 terms <= its own bucket)
No ActivitySnapshot window contributes seconds to more than one of {MeetingBar, Focus, OtherActive, Idle}
Meeting (headline) may be >= MeetingBar/60 (never less) - the gap is off-device meeting time
```

### Worked 8-hour example (must match a test fixture exactly)

```
Worked (attendance, unrelated pipeline) = 480m

Windows for the day (all 60s ActivitySnapshot windows unless noted):
  - 20 windows (20 min) overlap a true MeetingSignal window. Of these, 15 windows have
    ActiveSeconds=60 (fully active - typing notes in the meeting), 5 windows have
    ActiveSeconds=0 (fully idle - just listening). MeetingBarSeconds = 20*60 = 1200s = 20m.
  - MeetingSignal rows for the day: 12 true samples -> MeetingHeadlineMinutes = 12*2 = 24m
    (4 extra minutes of meeting signal with NO overlapping ActivitySnapshot row at all -
    e.g. tray briefly not running - genuinely off-device meeting time)
  - 130 non-meeting-overlapping windows (130 min) are ActiveSeconds=60, ForegroundProcessName="code.exe",
    forming one continuous 130-min Focus streak (>=30min threshold). All "code.exe" -> Productive category.
    FocusSecondsTotal = 130*60 = 7800s = 130m. ProductiveFocusSeconds = 7800s = 130m (all of it, Productive app).
  - 180 non-meeting-overlapping windows (180 min) are ActiveSeconds=60, not in a focus streak
    (mixed short-lived apps). Of these, 60 windows (60m) are Productive-classified apps
    (Excel, Outlook), 120 windows (120m) are Personal/Unknown.
    OtherActiveSecondsTotal = 180*60=10800s=180m. ProductiveOtherActiveSeconds = 60*60=3600s=60m.
  - 40 non-meeting-overlapping windows (40m) are IdleSeconds=60 (fully idle, no meeting).
    IdleSecondsTotal = 40*60=2400s=40m.

Totals:
  Focus       = 130m
  OtherActive = 180m
  Idle        = 40m
  MeetingBar  = 20m
  Meeting (headline) = 24m
  Observed    = 130+180+40+20 = 370m
  EngagedTime = 130+180+20 = 330m  (= Observed - Idle = 370-40 = 330 [OK])
  Productive  = 20 (MeetingBar) + 130 (ProductiveFocus) + 60 (ProductiveOtherActive) = 210m
              <= EngagedTime (330m) [OK]
  ProductiveRate = 210/330 = 63.6% -> "64%"
  MeetingOffDeskMinutes = max(0, 24-20) = 4m  -> caption shown: "4 min of your meeting time
                                                  happened away from your desk"
```

---

### Task 1: Shared `WorkPatternWindowClassifier` — replaces both duplicated Focus implementations

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/WorkPatternWindowClassifier.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternWindowClassifierTests.cs`

**Interfaces:**
- Consumes: `ActivitySnapshot` (existing entity, `src/ONEVO.Domain/Features/Monitoring/ActivityMonitoring/Entities/ActivitySnapshot.cs` — `Id, TenantId, EmployeeId, AgentDeviceId, CapturedAt, KeyboardEventsCount, MouseEventsCount, ActiveSeconds, IdleSeconds, IntensityScore, ForegroundProcessName, CreatedAt`), `MeetingSignal` (existing entity, `src/ONEVO.Domain/Features/Monitoring/Meetings/Entities/MeetingSignal.cs` — `Id, TenantId, EmployeeId, AgentDeviceId, CapturedAt, IsMeetingAppRunning, ProcessName, CreatedAt`), `AppCategoryClassifier.Classify(string?)` (existing, `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/AppCategoryClassifier.cs` — but this is in the **Infrastructure** project; since this classifier lives in **Application**, it cannot reference Infrastructure directly — see Step 3 note on where `AppCategory`/`AppCategoryClassifier` actually need to live).
- Produces: `WorkPatternWindowClassifier.Classify(IReadOnlyList<ActivitySnapshot> snapshots, IReadOnlyList<MeetingSignal> meetingSignals) → WorkPatternTotals` where:
  ```csharp
  public sealed record WorkPatternTotals(
      int FocusMinutes, int OtherActiveMinutes, int IdleMinutes, int MeetingBarMinutes,
      int ProductiveFocusMinutes, int ProductiveOtherActiveMinutes);
  ```
  This is the ONLY type later tasks consume from this file. `MeetingHeadlineMinutes` is computed separately by callers (it's a one-line `count*2`, doesn't need the classifier).

**Note on `AppCategoryClassifier`'s project location:** it currently lives in `ONEVO.Infrastructure` (`src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/AppCategoryClassifier.cs`), but `WorkPatternWindowClassifier` must live in `ONEVO.Application` (it's pure domain logic consumed by a query handler, matching where `ActivityTimelineBuilder` already lives). Application cannot depend on Infrastructure (wrong dependency direction in this codebase's layering — Infrastructure depends on Application, never the reverse). **Move `AppCategory` enum and `AppCategoryClassifier` class to `ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/AppCategoryClassifier.cs`** (same namespace pattern as `ActivityTimelineBuilder`), keeping the exact same hardcoded lists and `Classify` signature unchanged — this is a pure move, not a behavior change, and fixes a pre-existing layering issue this task's dependency would otherwise expose. Update the one existing consumer, `ActivityDailySummaryAggregator.cs`'s `AggregateAppUsage` method (in Infrastructure), to reference the new Application-layer namespace via a `using` statement instead.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternWindowClassifierTests.cs
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
        // 180 minutes of OtherActive: 60 Productive-classified, 120 Personal/Unknown.
        for (var i = 0; i < 180; i++)
        {
            cursor = cursor.AddMinutes(1);
            snaps.Add(Snap(cursor, activeSeconds: 60, idleSeconds: 0, process: i < 60 ? "outlook.exe" : "spotify.exe"));
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
        Assert.Equal(result.FocusMinutes + result.OtherActiveMinutes + result.IdleMinutes + result.MeetingBarMinutes,
            370); // Observed
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter WorkPatternWindowClassifierTests --nologo`
Expected: FAIL — compile error, `WorkPatternWindowClassifier` doesn't exist yet.

- [ ] **Step 3: Move `AppCategory`/`AppCategoryClassifier` to the Application layer**

Create `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/AppCategoryClassifier.cs` with the exact content currently in `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/AppCategoryClassifier.cs` (just the namespace changes):

```csharp
namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

public enum AppCategory { Productive, Personal, Unknown }

/// <summary>
/// Hardcoded process-name -> category lookup for productivity reporting.
/// Not tenant-configurable (YAGNI per 2026-08-17 Reports &amp; Analytics design spec) -
/// extend this list or add tenant overrides later if requested.
/// </summary>
public static class AppCategoryClassifier
{
    private static readonly HashSet<string> ProductiveProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code.exe", "devenv.exe", "excel.exe", "winword.exe", "powerpnt.exe",
        "outlook.exe", "teams.exe", "slack.exe", "figma.exe", "postman.exe",
        "notepad++.exe", "sourcetree.exe", "dbeaver.exe"
    };

    private static readonly HashSet<string> PersonalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.exe", "steam.exe", "discord.exe", "netflix.exe", "whatsapp.exe", "epicgameslauncher.exe"
    };

    public static AppCategory Classify(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return AppCategory.Unknown;
        if (ProductiveProcesses.Contains(processName)) return AppCategory.Productive;
        if (PersonalProcesses.Contains(processName)) return AppCategory.Personal;
        return AppCategory.Unknown;
    }
}
```

Delete `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/AppCategoryClassifier.cs`.

In `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregator.cs`, add the using statement `using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;` (it already implicitly used the old Infrastructure-namespace `AppCategoryClassifier`/`AppCategory` via being in the same file's namespace — now needs the explicit import). No other change to that file in this step (its `AggregateAppUsage` method keeps working exactly as before, just resolving the type from its new home).

Check for any other reference (test files) to the old `ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring.AppCategoryClassifier`/`AppCategory` namespace and update their `using` statements the same way — search: `grep -rn "AppCategoryClassifier\|AppCategory\." tests/ src/` and fix every hit's `using` line.

- [ ] **Step 4: Implement `WorkPatternWindowClassifier`**

```csharp
// src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/WorkPatternWindowClassifier.cs
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

public sealed record WorkPatternTotals(
    int FocusMinutes, int OtherActiveMinutes, int IdleMinutes, int MeetingBarMinutes,
    int ProductiveFocusMinutes, int ProductiveOtherActiveMinutes);

/// <summary>
/// Single source of truth for turning a day's raw ActivitySnapshot/MeetingSignal rows into
/// Focus/OtherActive/Idle/Meeting/Productive minutes. Used identically by the live "today"
/// query path (GetMyWorkPatternQueryHandler) and the nightly aggregation job
/// (ActivityDailySummaryAggregator) so the two paths can never compute different numbers for
/// the same underlying data - see WorkPatternLiveVsNightlyConsistencyTests.
///
/// Model summary (full derivation in docs/superpowers/plans/2026-09-25-work-pattern-productivity-model.md):
/// - A snapshot window that overlaps a meeting contributes its WHOLE duration (active+idle
///   both) to MeetingBar and is excluded from everything else below - meeting-ness doesn't
///   depend on whether you were typing.
/// - Duration allocation always uses ActiveSeconds/IdleSeconds specifically, never the raw
///   window width, since a single snapshot can be partially active and partially idle.
/// - Focus is a 30+ minute contiguous same-foreground-process streak among non-meeting windows.
/// - "Focus" does not imply "Productive" - a long streak in a Personal-classified app is real
///   Focus (a real behavioral/concentration signal) but doesn't count toward ProductiveFocus.
/// - "Unknown" app category is unclassified data, not a negative judgment - it simply doesn't
///   contribute to Productive, the same as it wouldn't contribute either way if we had no data
///   at all for that process.
/// </summary>
public static class WorkPatternWindowClassifier
{
    public const int FocusThresholdMinutes = 30;
    private static readonly TimeSpan MeetingSampleWindow = TimeSpan.FromMinutes(2);

    public static WorkPatternTotals Classify(
        IReadOnlyList<ActivitySnapshot> snapshots,
        IReadOnlyList<MeetingSignal> meetingSignals)
    {
        var meetingWindows = meetingSignals
            .Where(m => m.IsMeetingAppRunning)
            .Select(m => (Start: m.CapturedAt - MeetingSampleWindow, End: m.CapturedAt))
            .ToList();

        var ordered = snapshots
            .Where(s => s.ActiveSeconds + s.IdleSeconds > 0)
            .OrderBy(s => s.CapturedAt)
            .ToList();

        long meetingBarSeconds = 0, idleSeconds = 0, focusSeconds = 0, otherActiveSeconds = 0;
        long productiveFocusSeconds = 0, productiveOtherActiveSeconds = 0;

        // Windows NOT overlapping a meeting, in order - the population the focus-streak walk runs over.
        var nonMeetingWindows = new List<ActivitySnapshot>();

        foreach (var snap in ordered)
        {
            var windowDuration = TimeSpan.FromSeconds(snap.ActiveSeconds + snap.IdleSeconds);
            var windowStart = snap.CapturedAt - windowDuration;
            var windowEnd = snap.CapturedAt;

            var overlapsMeeting = meetingWindows.Any(m => windowStart < m.End && m.Start < windowEnd);
            if (overlapsMeeting)
            {
                meetingBarSeconds += snap.ActiveSeconds + snap.IdleSeconds;
                continue;
            }

            idleSeconds += snap.IdleSeconds;
            nonMeetingWindows.Add(snap);
        }

        // 30+ minute contiguous same-foreground-process streak, walked over non-meeting windows only.
        string? streakProcess = null;
        long streakActiveSeconds = 0;
        var streakWindows = new List<ActivitySnapshot>();

        void FlushStreak()
        {
            var minutes = streakActiveSeconds / 60;
            if (minutes >= FocusThresholdMinutes)
            {
                focusSeconds += streakActiveSeconds;
                foreach (var w in streakWindows)
                {
                    if (AppCategoryClassifier.Classify(w.ForegroundProcessName) == AppCategory.Productive)
                        productiveFocusSeconds += w.ActiveSeconds;
                }
            }
            else
            {
                otherActiveSeconds += streakActiveSeconds;
                foreach (var w in streakWindows)
                {
                    if (AppCategoryClassifier.Classify(w.ForegroundProcessName) == AppCategory.Productive)
                        productiveOtherActiveSeconds += w.ActiveSeconds;
                }
            }
            streakActiveSeconds = 0;
            streakWindows.Clear();
            streakProcess = null;
        }

        foreach (var snap in nonMeetingWindows)
        {
            if (snap.ActiveSeconds <= 0)
            {
                FlushStreak();
                continue;
            }

            var process = snap.ForegroundProcessName ?? string.Empty;
            if (streakProcess is null)
            {
                streakProcess = process;
            }
            else if (!string.Equals(streakProcess, process, StringComparison.OrdinalIgnoreCase))
            {
                FlushStreak();
                streakProcess = process;
            }

            streakActiveSeconds += snap.ActiveSeconds;
            streakWindows.Add(snap);
        }
        FlushStreak();

        return new WorkPatternTotals(
            FocusMinutes: (int)(focusSeconds / 60),
            OtherActiveMinutes: (int)(otherActiveSeconds / 60),
            IdleMinutes: (int)(idleSeconds / 60),
            MeetingBarMinutes: (int)(meetingBarSeconds / 60),
            ProductiveFocusMinutes: (int)(productiveFocusSeconds / 60),
            ProductiveOtherActiveMinutes: (int)(productiveOtherActiveSeconds / 60));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "WorkPatternWindowClassifierTests|AppCategoryClassifier"  --nologo`
Expected: PASS, all tests including the 8-hour worked example.

- [ ] **Step 6: Run full suite to confirm the AppCategoryClassifier move didn't break anything**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo` (must succeed — catches any missed `using` update)
Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: full suite green.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/WorkPatternWindowClassifier.cs src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/AppCategoryClassifier.cs src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregator.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternWindowClassifierTests.cs
git rm src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/AppCategoryClassifier.cs
git commit -m "feat(monitoring): add WorkPatternWindowClassifier, the single source of truth for Focus/Meeting/Productive minutes"
```

---

### Task 2: Retire the old duplicated Focus implementations

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityTimelineBuilder.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityTimelineBuilderTests.cs` (update, do not delete — this class still builds the Today's-Activity timeline segments, it just stops computing Focus minutes itself)
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregatorTests.cs` (update)

**Interfaces:**
- Consumes: `WorkPatternWindowClassifier.Classify` (Task 1), `WorkPatternTotals` (Task 1).
- Produces: `ActivityDailySummaryAggregator.Aggregate(...)` now sets `ActivityDailySummary.FocusMinutes`/`.TotalMeetingMinutes`/`.ProductiveAppMinutes` (repurposed field, see below) from the shared classifier's output, not its own `ComputeFocus`/`AggregateAppUsage` methods.

**Important finding from this session's investigation:** `ActivityTimelineBuilder`'s OLD focus-streak logic used **wall-clock span** (`streakEnd - streakStart`) to decide the 30-minute threshold and reported minutes, while `ActivityDailySummaryAggregator.ComputeFocus` used **summed `ActiveSeconds`**. These are NOT the same number whenever a streak contains a partially-idle snapshot — the wall-clock version over-counts. The new shared classifier uses the `ActiveSeconds`-sum approach (proven correct in Task 1's tests) — this is a real behavior fix, not just a refactor, and past-day `FocusMinutes` values already stored in `activity_daily_summary` rows may shift slightly on the next nightly run. This is expected and correct; do not try to preserve the old wall-clock numbers.

- [ ] **Step 1: Write the failing test for `ActivityDailySummaryAggregator` using the shared classifier**

Read the existing `ActivityDailySummaryAggregatorTests.cs` first to match its exact fixture/mock conventions, then add:

```csharp
[Fact]
public void Aggregate_UsesSharedClassifier_ForFocusMeetingAndProductive()
{
    // Mirrors WorkPatternWindowClassifierTests.Classify_ThirtyMinuteSameProcessStreak_IsFocus,
    // proving the aggregator now delegates to the shared classifier instead of its own ComputeFocus.
    var start = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    var snaps = Enumerable.Range(1, 30)
        .Select(i => new ActivitySnapshot
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
            CapturedAt = start.AddMinutes(i), ActiveSeconds = 60, IdleSeconds = 0, ForegroundProcessName = "code.exe"
        })
        .ToList();

    var summary = ActivityDailySummaryAggregator.Aggregate(
        TenantId, EmployeeId, DateOnly.FromDateTime(start.Date), snaps, DateTimeOffset.UtcNow);

    Assert.Equal(30, summary.FocusMinutes);
    Assert.Equal(30, summary.ProductiveAppMinutes); // repurposed: now "ProductiveFocus + ProductiveOtherActive" for the day
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter Aggregate_UsesSharedClassifier_ForFocusMeetingAndProductive --nologo`
Expected: FAIL (old `ComputeFocus`-based aggregator gives the old, possibly-different wall-clock-based number, or the test doesn't compile yet if `ProductiveAppMinutes` semantics changed).

- [ ] **Step 3: Update `ActivityDailySummaryAggregator.Aggregate`**

Replace the body of `Aggregate` (the calls to `ComputeFocus` and `AggregateAppUsage`/`totalMeetingMinutes`) with a call to the shared classifier. Read the full current file first (`src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregator.cs`) to preserve every other field it sets (`ActivePercentage`, `IntensityAvg`, `TopAppsJson`, `KeyboardTotal`, `MouseTotal`, `DataCoveragePercentage`, `ActivityScore`, `DeepFocusSessionsCount` etc. — none of those change in this task). Only the Focus/Meeting/Productive-related lines change:

```csharp
        var meetingSignals = /* existing meetingSignals parameter, already passed into Aggregate */ ?? [];
        var classified = ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services.WorkPatternWindowClassifier.Classify(ordered, meetingSignals);

        var focusMinutes = classified.FocusMinutes;
        var deepFocusSessions = classified.FocusMinutes >= WorkPatternWindowClassifier.FocusThresholdMinutes ? 1 : 0; // one streak per day in this simplified count, unchanged cardinality from before
        var totalMeetingMinutes = (meetingSignals).Count(s => s.IsMeetingAppRunning) * 2; // unchanged headline formula - independent of the classifier
        var productiveAppMinutes = classified.ProductiveFocusMinutes + classified.ProductiveOtherActiveMinutes; // repurposed field - see note below
```
Remove the now-unused `ComputeFocus` method and the `focusMinutes`/`deepFocusSessions` tuple it returned. Keep `AggregateAppUsage`/`AppUsageSnapshot`-based `PersonalAppMinutes`/`UnknownAppMinutes`/`TopAppsJson` computation exactly as-is (still used by the separate, out-of-scope Productivity Report feature) — only `ProductiveAppMinutes`'s VALUE now comes from the shared classifier instead of `AggregateAppUsage`'s app-usage-stream-based count. Add a one-line comment at the `ProductiveAppMinutes` assignment: `// Repurposed 2026-09-25: now sourced from WorkPatternWindowClassifier (ActivitySnapshot-based), not AggregateAppUsage's separate AppUsageSnapshot stream, so live "today" and nightly historical Productive numbers can never drift - see Task 3's consistency test.`

- [ ] **Step 4: Simplify `ActivityTimelineBuilder`**

`ActivityTimelineBuilder.BuildSegments` still needs to produce visual timeline segments (start/end boundaries) for the Today's Activity card — that job doesn't go away. But it should stop hand-computing the 30-minute focus threshold itself; instead it should mark a streak as Focus using the SAME threshold constant, sourced from the shared classifier, to guarantee the two never drift on the threshold value itself even though one produces intervals and the other produces totals:

```csharp
// src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityTimelineBuilder.cs
// Change line 19's constant:
public const int FocusThresholdMinutes = WorkPatternWindowClassifier.FocusThresholdMinutes;
```
Add `using` for the classifier's namespace if not already present (same namespace, so no using needed — both are in `ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services`). No other change to this file — its own segment-building logic (producing `[start,end)` intervals for display) is a genuinely different job (visual timeline, not totals) and stays as-is; only the threshold constant is now single-sourced. Update the file's doc comment (currently says "Mirrors the focus-streak rule used by ActivityDailySummaryAggregator.ComputeFocus... but kept as its own isolated implementation") since `ComputeFocus` no longer exists:

```csharp
/// <summary>
/// Classifies a day's activity snapshots into Focus/Idle timeline segments with real
/// start/end boundaries, for self-service display. Uses the same 30-minute threshold as
/// WorkPatternWindowClassifier (which computes the authoritative Focus/Meeting/Productive
/// totals) but is a separate implementation because this one needs interval boundaries for
/// the Today's Activity timeline visualization, not just totals.
/// </summary>
```

- [ ] **Step 5: Update `ActivityTimelineBuilderTests.cs`**

Read the existing test file first. Its assertions on segment TYPE/threshold behavior should be unaffected (same 30-min rule, same wall-clock-span-based interval boundaries — this file's own segment-building logic didn't change, only where the threshold constant comes from). Just verify the full file still compiles and passes; no new tests required for this step since Task 1 already covers the classifier's own correctness and this task's only functional change is the constant's source.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "ActivityDailySummaryAggregatorTests|ActivityTimelineBuilderTests" --nologo`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityTimelineBuilder.cs src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregator.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregatorTests.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityTimelineBuilderTests.cs
git commit -m "refactor(monitoring): retire duplicated Focus algorithms in favor of the shared classifier"
```

---

### Task 3: Wire the classifier into `GetMyWorkPatternQueryHandler` (live "today" path) + invariant and live-vs-nightly consistency tests

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetMyWorkPattern/GetMyWorkPatternQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/WorkPatternResponse.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/GetMyWorkPatternQueryHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternInvariantTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternLiveVsNightlyConsistencyTests.cs`

**Interfaces:**
- Consumes: `WorkPatternWindowClassifier.Classify` (Task 1), `ActivityDailySummaryAggregator.Aggregate` (Task 2, for the nightly-path side of the consistency test).
- Produces: `WorkPatternDayDto(DateOnly Date, int FocusMinutes, int MeetingMinutes, int OtherActiveMinutes, int IdleMinutes, int ProductiveMinutes)` — **renamed** `AdminMinutes`→`OtherActiveMinutes`, **added** `ProductiveMinutes`. This is the exact contract Task 4 (API contract), Task 5 (frontend model), and Task 6 (frontend component) consume — the field names here are load-bearing for all three.

- [ ] **Step 1: Update the DTO**

```csharp
// src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/WorkPatternResponse.cs
namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record WorkPatternDayDto(
    DateOnly Date,
    int FocusMinutes,
    int MeetingMinutes,
    int OtherActiveMinutes,
    int IdleMinutes,
    int ProductiveMinutes);

public sealed record WorkPatternResponse(IReadOnlyList<WorkPatternDayDto> Days);
```

- [ ] **Step 2: Update the existing handler tests for the renamed/new fields (TDD: these must fail first)**

In `GetMyWorkPatternQueryHandlerTests.cs`, every existing assertion on `day.AdminMinutes` becomes `day.OtherActiveMinutes`, and every test gets a new `day.ProductiveMinutes` assertion. Read the current file (already read this session — 7 tests: `Handle_Unauthenticated_ReturnsForbidden`, `Handle_NoEmployeeRecord_ReturnsForbidden`, `Handle_PastDayUsesAggregatedSummaryAndDerivesAdminMinutes`, `Handle_PastDayWithNoSummaryRow_DefaultsToAllZero`, `Handle_FutureDay_IsAlwaysZero`, `Handle_Today_ComputesLiveFromSnapshotsAndMeetingSignals`, `Handle_OverlappingFocusAndMeetingMinutes_ClampsAdminAtZero`, `Handle_MultiDayRange_ReturnsOneEntryPerDayInOrder`).

Rename the test `Handle_PastDayUsesAggregatedSummaryAndDerivesAdminMinutes` → `Handle_PastDayUsesAggregatedSummaryAndDerivesOtherActiveMinutes`, update its body:

```csharp
[Fact]
public async Task Handle_PastDayUsesAggregatedSummaryAndDerivesOtherActiveMinutes()
{
    var sut = BuildSut();
    var pastDay = Today.AddDays(-2);
    _summaries.Setup(x => x.GetRangeAsync(TenantId, EmployeeId, pastDay, pastDay, It.IsAny<CancellationToken>()))
        .ReturnsAsync([
            new ActivityDailySummary
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = pastDay,
                TotalActiveMinutes = 400, FocusMinutes = 180, TotalMeetingMinutes = 60,
                TotalIdleMinutes = 45, ProductiveAppMinutes = 150, CreatedAt = DateTimeOffset.UtcNow
            }
        ]);

    var result = await sut.Handle(new GetMyWorkPatternQuery(pastDay, pastDay), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    var day = result.Value!.Days.Should().ContainSingle().Subject;
    day.FocusMinutes.Should().Be(180);
    day.MeetingMinutes.Should().Be(60);
    day.OtherActiveMinutes.Should().Be(160); // 400 - 180 - 60, same subtraction as before - the MeetingBar/headline
                                              // split only matters for the LIVE path's own MeetingBar-vs-Active
                                              // exclusion; the persisted summary's TotalActiveMinutes already
                                              // excludes meeting-overlapping windows per Task 2's aggregator update.
    day.IdleMinutes.Should().Be(45);
    day.ProductiveMinutes.Should().Be(150); // straight passthrough of the persisted, pre-aggregated value
}
```

Rename `Handle_OverlappingFocusAndMeetingMinutes_ClampsAdminAtZero` → `Handle_OverlappingFocusAndMeetingMinutes_ClampsOtherActiveAtZero`, change its `AdminMinutes` assertion to `OtherActiveMinutes`. Update `Handle_PastDayWithNoSummaryRow_DefaultsToAllZero` and `Handle_FutureDay_IsAlwaysZero` to assert `OtherActiveMinutes` (renamed) instead of `AdminMinutes`, and add `day.ProductiveMinutes.Should().Be(0);` to each.

Rewrite `Handle_Today_ComputesLiveFromSnapshotsAndMeetingSignals` — its fixture's expected `MeetingMinutes` value changes because Meeting is no longer gated on activity but IS now excluded from the Focus/Idle split when overlapping (behavior didn't change for THIS fixture's headline Meeting number, since headline Meeting stays the old independent formula — but note the meeting signal in this fixture is captured at `baseTime`, and the snapshots start at `baseTime+5min`, so under the new window-overlap logic for MeetingBar/exclusion, none of the snapshots actually overlap the meeting's `[baseTime-2min, baseTime)` window; this fixture was never exercising the meeting-overlap-with-activity path to begin with, so its Focus/OtherActive numbers are unaffected):

```csharp
[Fact]
public async Task Handle_Today_ComputesLiveFromSnapshotsAndMeetingSignals()
{
    var sut = BuildSut();
    var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
    var snaps = Enumerable.Range(0, 6)
        .Select(i => new ActivitySnapshot
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
            CapturedAt = baseTime.AddMinutes((i + 1) * 5), ActiveSeconds = 300, IdleSeconds = 0,
            ForegroundProcessName = "code.exe", CreatedAt = baseTime
        })
        .Append(new ActivitySnapshot
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
            CapturedAt = baseTime.AddMinutes(35), ActiveSeconds = 0, IdleSeconds = 300,
            ForegroundProcessName = null, CreatedAt = baseTime
        })
        .ToList();
    _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, Today, It.IsAny<CancellationToken>()))
        .ReturnsAsync(snaps);
    _meetings.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, Today, It.IsAny<CancellationToken>()))
        .ReturnsAsync([
            new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = baseTime, IsMeetingAppRunning = true, CreatedAt = baseTime },
            new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = baseTime, IsMeetingAppRunning = false, CreatedAt = baseTime }
        ]);

    var result = await sut.Handle(new GetMyWorkPatternQuery(Today, Today), CancellationToken.None);

    var day = result.Value!.Days.Should().ContainSingle().Subject;
    day.FocusMinutes.Should().Be(30);       // unaffected - meeting signal doesn't overlap any snapshot window
    day.MeetingMinutes.Should().Be(2);       // headline formula unchanged: 1 true sample * 2min
    day.OtherActiveMinutes.Should().Be(0);   // 30 active minutes total, all in the focus streak
    day.IdleMinutes.Should().Be(5);
    day.ProductiveMinutes.Should().Be(30);   // code.exe is Productive-classified -> all 30 focus minutes count
    _summaries.Verify(x => x.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetMyWorkPatternQueryHandlerTests --nologo`
Expected: FAIL — compile errors (`AdminMinutes` no longer exists, `ProductiveMinutes` doesn't exist yet, handler still returns the old DTO shape).

- [ ] **Step 4: Update `GetMyWorkPatternQueryHandler`**

Replace the file's `BuildTodayDtoAsync` and `ToDto` methods:

```csharp
// src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetMyWorkPattern/GetMyWorkPatternQueryHandler.cs
// (keep the existing using statements, add:)
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

// ... Handle() method: change every call site building a WorkPatternDayDto to go through the new
// ToDto signature below. The main Handle() loop's three call sites become:
//   days.Add(todayDto);                                                          // unchanged
//   days.Add(new WorkPatternDayDto(date, 0, 0, 0, 0, 0));                        // future day - one more 0
//   days.Add(ToDto(date, summary.FocusMinutes, summary.TotalMeetingMinutes,
//       summary.TotalActiveMinutes, summary.TotalIdleMinutes, summary.ProductiveAppMinutes)); // past day

private async Task<WorkPatternDayDto> BuildTodayDtoAsync(
    Guid tenantId, Guid employeeId, DateOnly today, CancellationToken ct)
{
    var snaps = await snapshots.GetAllByEmployeeDateAsync(tenantId, employeeId, today, ct);
    var meetingSignals = await meetings.GetAllByEmployeeDateAsync(tenantId, employeeId, today, ct);

    var classified = WorkPatternWindowClassifier.Classify(snaps, meetingSignals);
    var meetingHeadlineMinutes = meetingSignals.Count(s => s.IsMeetingAppRunning) * MeetingMinutesPerSample;
    var productiveMinutes = classified.ProductiveFocusMinutes + classified.ProductiveOtherActiveMinutes
        + classified.MeetingBarMinutes; // Productive credits the OBSERVED portion of meeting time (MeetingBar),
                                         // not the headline off-device-inclusive number - see plan doc's
                                         // "EngagedTime" derivation for why.

    return new WorkPatternDayDto(
        today, classified.FocusMinutes, meetingHeadlineMinutes, classified.OtherActiveMinutes,
        classified.IdleMinutes, productiveMinutes);
}

private static WorkPatternDayDto ToDto(
    DateOnly date, int focusMinutes, int meetingMinutes, int activeMinutes, int idleMinutes, int productiveMinutes)
{
    // Past-day path: ActivityDailySummary.TotalActiveMinutes/FocusMinutes/TotalMeetingMinutes/
    // ProductiveAppMinutes were already computed by the nightly job via the SAME
    // WorkPatternWindowClassifier (Task 2), so this is a straight passthrough/subtraction, not a
    // second independent calculation - see WorkPatternLiveVsNightlyConsistencyTests for the proof
    // that the live and nightly paths agree.
    var otherActiveMinutes = Math.Max(0, activeMinutes - focusMinutes - meetingMinutes);
    return new WorkPatternDayDto(date, focusMinutes, meetingMinutes, otherActiveMinutes, idleMinutes, productiveMinutes);
}
```

Note: `activeMinutes` passed into `ToDto` for the past-day path is `summary.TotalActiveMinutes`, which after Task 2's aggregator update already represents `Focus+OtherActive` (meeting-overlapping windows excluded) — the subtraction here stays structurally the same shape as before, just now operating on numbers that come from the shared classifier instead of the old duplicated one.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetMyWorkPatternQueryHandlerTests --nologo`
Expected: PASS, all 8 tests.

- [ ] **Step 6: Write the invariant tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternInvariantTests.cs
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

/// <summary>Property-style checks over the 8-hour worked example and a few edge fixtures,
/// proving the invariants stated in docs/superpowers/plans/2026-09-25-work-pattern-productivity-model.md
/// hold structurally rather than via a clamp.</summary>
public sealed class WorkPatternInvariantTests
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

    [Theory]
    [InlineData(0)]    // no snapshots
    [InlineData(1)]    // single partial-active/idle snapshot
    [InlineData(30)]   // exactly one focus streak
    [InlineData(60)]   // two mixed streaks
    public void Classify_AnyFixtureSize_ObservedEqualsSumOfFourBuckets(int minuteCount)
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, minuteCount)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: i % 3 == 0 ? 0 : 60, idleSeconds: i % 3 == 0 ? 60 : 0,
                process: i % 2 == 0 ? "code.exe" : "excel.exe"))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        var observed = result.FocusMinutes + result.OtherActiveMinutes + result.IdleMinutes + result.MeetingBarMinutes;
        Assert.Equal(minuteCount, observed); // every minute lands in exactly one bucket
    }

    [Fact]
    public void Classify_ProductiveNeverExceedsEngagedTime()
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, 100)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: 60, idleSeconds: 0, process: "code.exe"))
            .ToList();
        var meetings = Enumerable.Range(1, 20)
            .Select(i => Meeting(start.AddMinutes(i), running: true))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);
        var engagedTime = result.FocusMinutes + result.OtherActiveMinutes + result.MeetingBarMinutes;
        var productive = result.MeetingBarMinutes + result.ProductiveFocusMinutes + result.ProductiveOtherActiveMinutes;

        Assert.True(productive <= engagedTime);
    }

    [Fact]
    public void Classify_NoWindowContributesToMoreThanOneBucket()
    {
        // A window overlapping a meeting must NEVER also show up in Focus/OtherActive/Idle -
        // verified by checking total seconds allocated never exceeds total seconds fed in.
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = new[]
        {
            Snap(start.AddMinutes(1), activeSeconds: 40, idleSeconds: 20, process: "teams.exe"),
        };
        var meetings = new[] { Meeting(start.AddMinutes(1), running: true) };

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);

        Assert.Equal(1, result.MeetingBarMinutes); // whole 60s window
        Assert.Equal(0, result.FocusMinutes);
        Assert.Equal(0, result.OtherActiveMinutes);
        Assert.Equal(0, result.IdleMinutes);
    }
}
```

- [ ] **Step 7: Run invariant tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter WorkPatternInvariantTests --nologo`
Expected: PASS. (These should pass immediately since Task 1's implementation already satisfies them — this step is about locking the guarantee with a permanent regression test, not fixing new bugs.)

- [ ] **Step 8: Write the live-vs-nightly consistency test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternLiveVsNightlyConsistencyTests.cs
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
```

- [ ] **Step 9: Run the consistency test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter WorkPatternLiveVsNightlyConsistencyTests --nologo`
Expected: PASS. If it fails, that means Task 2's aggregator update and this task's handler update diverged somewhere — do not proceed to Task 4 until this passes for real (not by special-casing either path).

- [ ] **Step 10: Full backend suite**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: 0 errors, full suite green.

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetMyWorkPattern/GetMyWorkPatternQueryHandler.cs src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/WorkPatternResponse.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/GetMyWorkPatternQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternInvariantTests.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternLiveVsNightlyConsistencyTests.cs
git commit -m "feat(monitoring): wire the shared classifier into the live work-pattern query, add invariant + live-vs-nightly consistency tests"
```

This is the last backend task. Backend is now fully correct end to end.

---

### Task 4: Frontend model + API service update

**Files:**
- Modify: `src/app/modules/dashboard/models/work-pattern.model.ts`
- Modify: `src/app/modules/dashboard/data-access/work-pattern-api.service.ts`
- Test: `src/app/modules/dashboard/data-access/work-pattern-api.service.spec.ts`

**Interfaces:**
- Consumes: backend `WorkPatternDayDto(Date, FocusMinutes, MeetingMinutes, OtherActiveMinutes, IdleMinutes, ProductiveMinutes)` (Task 3).
- Produces: `WorkPatternDay { date, focusMinutes, meetingMinutes, otherActiveMinutes, idleMinutes, productiveMinutes }` — renamed/added fields Task 5 (component) consumes directly.

- [ ] **Step 1: Read the current model and service files**

Read `work-pattern.model.ts` and `work-pattern-api.service.ts` in full first (both were already located this session but not read in full) to see their exact current shape before editing.

- [ ] **Step 2: Write the failing test**

In `work-pattern-api.service.spec.ts`, find the existing test asserting the parsed response shape and update/add:

```typescript
it('getMyWorkPattern returns days with otherActiveMinutes and productiveMinutes fields', () => {
  service.getMyWorkPattern('2026-09-01', '2026-09-01').subscribe((response) => {
    expect(response.days[0].otherActiveMinutes).toBe(160);
    expect(response.days[0].productiveMinutes).toBe(150);
  });
  const req = httpMock.expectOne((r) => r.url.includes('my-work-pattern'));
  req.flush({
    days: [{ date: '2026-09-01', focusMinutes: 180, meetingMinutes: 60, otherActiveMinutes: 160, idleMinutes: 45, productiveMinutes: 150 }]
  });
});
```
(Match `httpMock`/`service` variable names to whatever the existing spec file already uses.)

- [ ] **Step 3: Run test to verify it fails**

Run: `npm test -- --include='**/work-pattern-api.service.spec.ts'` (from the frontend repo root)
Expected: FAIL — `WorkPatternDay` model doesn't have `otherActiveMinutes`/`productiveMinutes` yet (`adminMinutes` still there instead).

- [ ] **Step 4: Update the model**

```typescript
// src/app/modules/dashboard/models/work-pattern.model.ts
export interface WorkPatternDay {
  readonly date: string;
  readonly focusMinutes: number;
  readonly meetingMinutes: number;
  readonly otherActiveMinutes: number;
  readonly idleMinutes: number;
  readonly productiveMinutes: number;
}

export interface WorkPatternResponse {
  readonly days: readonly WorkPatternDay[];
}
```
(Keep whatever other exports already exist in this file untouched — read the file first to confirm there's nothing else to preserve.)

- [ ] **Step 5: Verify the API service itself needs no change**

`work-pattern-api.service.ts`'s `getMyWorkPattern` almost certainly just does `this.http.get<WorkPatternResponse>(...)` with no field-by-field mapping — if so, no code change is needed there, the type change alone is sufficient. Confirm this by reading the file; only add a step here if it turns out to do manual field mapping.

- [ ] **Step 6: Run tests to verify they pass**

Run: `npm test -- --include='**/work-pattern-api.service.spec.ts'`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/dashboard/models/work-pattern.model.ts src/app/modules/dashboard/data-access/work-pattern-api.service.spec.ts
git commit -m "feat(dashboard): rename adminMinutes to otherActiveMinutes, add productiveMinutes to the work-pattern model"
```

---

### Task 5: `work-pattern-card` component — real Productive, exact bar, Observed Coverage caption, renamed label

**Files:**
- Modify: `src/app/modules/dashboard/feature/work-pattern-card/work-pattern-card.component.ts`
- Modify: `src/app/modules/dashboard/feature/work-pattern-card/work-pattern-card.component.html`
- Test: `src/app/modules/dashboard/feature/work-pattern-card/work-pattern-card.component.spec.ts` (create if it doesn't exist — check first)

**Interfaces:**
- Consumes: `WorkPatternDay` (Task 4), `WorkPatternApiService.getMyWorkPattern` (Task 4, unchanged signature).
- Produces: nothing new consumed by later tasks — this is a leaf UI component.

- [ ] **Step 1: Read the current component and template**

Both were already read in full earlier this session (`work-pattern-card.component.ts` fully read; `.html` not yet read this session — read it now before editing) and check for an existing `.spec.ts` file.

- [ ] **Step 2: Write the failing tests**

```typescript
// Add to work-pattern-card.component.spec.ts (create the file if none exists, following the
// conventions of a sibling dashboard component's spec file, e.g. today-at-a-glance-card's own
// spec if one exists — read that first to match TestBed setup style)

it('productiveMinutes comes straight from the API, not derived client-side', () => {
  component['rawDays'].set([
    { date: '2026-09-01', focusMinutes: 100, meetingMinutes: 20, otherActiveMinutes: 30, idleMinutes: 10, productiveMinutes: 90 }
  ]);
  fixture.detectChanges();

  expect(component['productiveMinutes']()).toBe(90); // not 100+20+30=150
});

it('segments sum to exactly 100% of Observed, no clamp needed', () => {
  component['rawDays'].set([
    { date: '2026-09-01', focusMinutes: 100, meetingMinutes: 20, otherActiveMinutes: 30, idleMinutes: 10, productiveMinutes: 90 }
  ]);
  // stub weeklyWorkedMinutes/Observed source as this file's existing tests already do
  fixture.detectChanges();

  const segments = component['segments']();
  const totalWidth = segments.reduce((sum: number, s: { widthPercent: number }) => sum + s.widthPercent, 0);
  expect(totalWidth).toBeCloseTo(100, 1);
});

it('segment label for admin/other reads "Other active"', () => {
  expect(component['segmentLabel']('admin')).toBe('Other active');
});

it('meeting label includes "(est.)"', () => {
  expect(component['segmentLabel']('meeting')).toBe('Meeting time (est.)');
});
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `npm test -- --include='**/work-pattern-card.component.spec.ts'`
Expected: FAIL.

- [ ] **Step 4: Rewrite the component**

Replace the computed properties and segment logic:

```typescript
// src/app/modules/dashboard/feature/work-pattern-card/work-pattern-card.component.ts
// (keep the existing imports, WorkPatternPeriod type, WORK_PATTERN_PERIOD_OPTIONS, class
//  scaffolding, ngOnInit/loadForPeriod/period-dropdown methods exactly as-is - only the
//  computed signals and segment/label logic below change)

  private readonly focusMinutes = computed(() => sumBy(this.rawDays(), (d) => d.focusMinutes));
  private readonly meetingMinutes = computed(() => sumBy(this.rawDays(), (d) => d.meetingMinutes));
  private readonly otherActiveMinutes = computed(() => sumBy(this.rawDays(), (d) => d.otherActiveMinutes));
  private readonly idleMinutes = computed(() => sumBy(this.rawDays(), (d) => d.idleMinutes));
  protected readonly productiveMinutes = computed(() => sumBy(this.rawDays(), (d) => d.productiveMinutes));

  protected readonly weeklyWorkedMinutes = computed(() =>
    this.store.history().reduce((sum, row) => sum + row.totalWorkedMinutes, 0)
  );

  protected readonly hasActivity = computed(() => this.weeklyWorkedMinutes() > 0);

  // Observed = Focus + Meeting + OtherActive + Idle (the bar's own denominator - these four
  // ARE disjoint by construction on the backend, see WorkPatternWindowClassifier, so this sum
  // is exact, never needing an overflow clamp).
  protected readonly observedMinutes = computed(
    () => this.focusMinutes() + this.meetingMinutes() + this.otherActiveMinutes() + this.idleMinutes()
  );

  protected readonly observedCoverageLabel = computed(() => {
    const worked = this.weeklyWorkedMinutes();
    const observed = this.observedMinutes();
    if (worked <= 0) return '';
    const percent = Math.round((observed / worked) * 100);
    return `${formatHours(observed)} observed of ${formatHours(worked)} worked · ${percent}%`;
  });

  // Productive Rate = Productive / (Observed - Idle), the "engaged time" denominator - not
  // Active, since headline Meeting minutes are independent of the ActivitySnapshot stream and
  // could otherwise push this above 100%. See the plan doc's "EngagedTime" derivation.
  protected readonly productiveRateLabel = computed(() => {
    const engaged = this.observedMinutes() - this.idleMinutes();
    if (engaged <= 0) return '—';
    const percent = Math.min(100, Math.round((this.productiveMinutes() / engaged) * 100));
    return `${percent}%`;
  });

  protected readonly weeklyWorkedLabel = computed(() => formatHours(this.weeklyWorkedMinutes()));
  protected readonly productiveLabel = computed(() => formatHours(this.productiveMinutes()));
  protected readonly focusLabel = computed(() => formatHours(this.focusMinutes()));
  protected readonly meetingLabel = computed(() => formatHours(this.meetingMinutes()));
  protected readonly adminLabel = computed(() => formatHours(this.otherActiveMinutes()));
  protected readonly idleLabel = computed(() => formatHours(this.idleMinutes()));

  protected readonly lastUpdatedLabel = computed(() => {
    const d = this.lastUpdated();
    return d
      ? new Intl.DateTimeFormat(undefined, {
          hour: 'numeric', minute: '2-digit', hour12: true, day: 'numeric', month: 'short'
        }).format(d)
      : '';
  });

  // Denominator = Observed (Active+Idle). Focus/Meeting/OtherActive/Idle are disjoint by
  // construction on the backend (WorkPatternWindowClassifier), so these four sum to EXACTLY
  // 100% of Observed - no scale/overflow clamp needed (the old
  // `scale = total>denominator ? denominator/total : 1` guard is intentionally removed).
  protected readonly segments = computed<WorkPatternSegment[]>(() => {
    const denominator = this.observedMinutes();
    if (denominator <= 0) {
      return [];
    }
    const raw: { type: WorkPatternSegmentType; minutes: number }[] = [
      { type: 'focus', minutes: this.focusMinutes() },
      { type: 'meeting', minutes: this.meetingMinutes() },
      { type: 'admin', minutes: this.otherActiveMinutes() },
      { type: 'idle', minutes: this.idleMinutes() }
    ];
    return raw
      .filter((s) => s.minutes > 0)
      .map((s) => ({ ...s, widthPercent: (s.minutes / denominator) * 100 }));
  });
```

Update `sumBy`'s call sites are unchanged (still a plain reducer helper at the bottom of the file — no change needed there). Update `SEGMENT_LABELS`:

```typescript
const SEGMENT_LABELS: Record<WorkPatternSegmentType, string> = {
  focus: 'Focus work',
  meeting: 'Meeting time (est.)',
  admin: 'Other active',
  idle: 'Idle time'
};
```

- [ ] **Step 5: Update the template**

Read `work-pattern-card.component.html` in full first. Add the Observed Coverage caption near the top of the card (exact placement/markup should match the file's existing structure/CSS classes — read it to place this consistently, e.g. near `lastUpdatedLabel`):

```html
@if (observedCoverageLabel()) {
  <p class="text-xs text-[var(--color-text-secondary)]">{{ observedCoverageLabel() }}</p>
}
```

Add a Productive Rate line under the Productive tile (find that tile's existing markup and add alongside `productiveLabel`):

```html
<span class="text-xs text-[var(--color-text-secondary)]">{{ productiveRateLabel() }} of active time</span>
```

Add a tooltip to the Meeting tile/segment (find its existing markup, add a `title` attribute or the component's existing tooltip pattern if one exists — check for a shared tooltip directive/component first rather than inventing a new pattern):

```html
title="Based on meeting-app window presence, not confirmed call participation."
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `npm test -- --include='**/work-pattern-card.component.spec.ts'`
Expected: PASS.

- [ ] **Step 7: Run the full frontend suite to confirm nothing else references the removed `adminMinutes` field or the old scale-clamp behavior**

Run: `npm test -- --watch=false`
Expected: no new failures beyond this session's already-known, unrelated pre-existing baseline (work.routes, notification-bell, member-management-popup — confirmed earlier this session).

- [ ] **Step 8: Commit**

```bash
git add src/app/modules/dashboard/feature/work-pattern-card/
git commit -m "feat(dashboard): real Productive/Productive Rate, exact Observed-based bar, Observed Coverage caption, renamed Other Active label"
```

---

### Task 6: Today's Activity — add a `meeting` timeline segment

**Files:**
- Modify: `src/app/modules/dashboard/util/timeline-segments.util.ts`
- Modify: `src/app/modules/dashboard/feature/today-at-a-glance-card/today-at-a-glance-card.component.ts`
- Test: `src/app/modules/dashboard/util/timeline-segments.util.spec.ts`

**Interfaces:**
- Consumes: `ActivityTimelineSegment` (existing model, extend with a `'meeting'` type — check `src/app/modules/dashboard/models/activity-timeline.model.ts` first for its current shape before editing).
- Produces: `TimelineSegment['type']` gains `'meeting'` alongside `working | break | focus | idle`.

- [ ] **Step 1: Read the current model and component**

Read `activity-timeline.model.ts` and `today-at-a-glance-card.component.ts` in full (the latter was described but not fully read this session) to see exactly how `ActivityTimelineApiService.getMyTimeline()`'s response is currently shaped and consumed, and whether the backend's `my-timeline` endpoint (`GetMyActivityTimelineQueryHandler`/`ActivityTimelineBuilder`) would need a corresponding change to emit meeting segments — **check whether `ActivityTimelineBuilder.BuildSegments` needs a `meetingSignals` parameter added to also emit `'meeting'`-typed segments** (currently it only takes `snapshots` and emits `focus`/`idle`). If so, this task must also touch:
- `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityTimelineBuilder.cs` — add an optional `IReadOnlyList<MeetingSignal>` parameter, and emit a `MeetingType = "meeting"` segment for windows overlapping a meeting (reusing the same overlap check pattern from `WorkPatternWindowClassifier`, Task 1 — do not re-derive a third implementation of meeting-window overlap; extract it to a small shared helper if this duplication would otherwise be a third copy. Given `WorkPatternWindowClassifier` already computes this exact overlap privately, expose a `public static bool WindowOverlapsAnyMeeting(DateTimeOffset windowStart, DateTimeOffset windowEnd, IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> meetingWindows)`-shaped helper method on it — or a small separate `MeetingWindowOverlap` static helper both classes call — rather than hand-copying the `Any(m => windowStart < m.End && m.Start < windowEnd)` check a third time.)
- `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetMyActivityTimeline/GetMyActivityTimelineQueryHandler.cs` — fetch `MeetingSignal`s for the day (mirroring how `GetMyWorkPatternQueryHandler` already does) and pass them into `BuildSegments`.
- The `ActivityTimelineSegmentDto`/API contract for `my-timeline` and its frontend model, to carry the new segment type through.

This is a real, non-trivial addition — read every file in this list before writing any test, since the exact current signatures matter for a clean diff.

- [ ] **Step 2: Write the failing backend test**

In `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityTimelineBuilderTests.cs`, add:

```csharp
[Fact]
public void BuildSegments_MeetingOverlappingWindow_EmitsMeetingSegment()
{
    var meetingAt = new DateTimeOffset(2026, 9, 25, 9, 10, 0, TimeSpan.Zero);
    var snapAt = new DateTimeOffset(2026, 9, 25, 9, 9, 0, TimeSpan.Zero); // window [09:08,09:09)
    var snaps = new[]
    {
        new ActivitySnapshot
        {
            Id = Guid.NewGuid(), CapturedAt = snapAt, ActiveSeconds = 40, IdleSeconds = 20,
            ForegroundProcessName = "teams.exe"
        }
    };
    var meetings = new[]
    {
        new MeetingSignal { Id = Guid.NewGuid(), CapturedAt = meetingAt, IsMeetingAppRunning = true }
    };

    var segments = ActivityTimelineBuilder.BuildSegments(snaps, meetings);

    var segment = Assert.Single(segments);
    Assert.Equal(ActivityTimelineBuilder.MeetingType, segment.Type);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter BuildSegments_MeetingOverlappingWindow_EmitsMeetingSegment --nologo`
Expected: FAIL — `BuildSegments` doesn't take a `meetings` parameter yet, `MeetingType` doesn't exist.

- [ ] **Step 4: Update `ActivityTimelineBuilder.cs`**

```csharp
// Add alongside FocusType/IdleType:
public const string MeetingType = "meeting";

// Change the signature and add meeting-overlap exclusion, mirroring
// WorkPatternWindowClassifier's own overlap check exactly (same 2-minute window math):
public static IReadOnlyList<ActivityTimelineSegmentDto> BuildSegments(
    IReadOnlyList<ActivitySnapshot> snapshots,
    IReadOnlyList<MeetingSignal>? meetingSignals = null)
{
    var meetingWindows = (meetingSignals ?? [])
        .Where(m => m.IsMeetingAppRunning)
        .Select(m => (Start: m.CapturedAt - TimeSpan.FromMinutes(2), End: m.CapturedAt))
        .ToList();

    var ordered = snapshots
        .Where(s => s.ActiveSeconds + s.IdleSeconds > 0)
        .OrderBy(s => s.CapturedAt)
        .ToList();

    var segments = new List<ActivityTimelineSegmentDto>();
    DateTimeOffset? streakStart = null;
    var streakEnd = default(DateTimeOffset);
    var streakActive = false;
    string streakProcess = string.Empty;

    void FlushStreak()
    {
        if (streakStart is null) return;
        var minutes = (streakEnd - streakStart.Value).TotalMinutes;
        var type = streakActive && minutes >= FocusThresholdMinutes ? FocusType : IdleType;
        segments.Add(new ActivityTimelineSegmentDto(streakStart.Value, streakEnd, type));
        streakStart = null;
    }

    foreach (var snapshot in ordered)
    {
        var duration = TimeSpan.FromSeconds(snapshot.ActiveSeconds + snapshot.IdleSeconds);
        var start = snapshot.CapturedAt - duration;
        var end = snapshot.CapturedAt;

        if (meetingWindows.Any(m => start < m.End && m.Start < end))
        {
            FlushStreak();
            segments.Add(new ActivityTimelineSegmentDto(start, end, MeetingType));
            continue;
        }

        var isActive = snapshot.ActiveSeconds > 0;
        var process = snapshot.ForegroundProcessName ?? string.Empty;

        var continuesStreak = streakStart is not null
            && isActive == streakActive
            && (!isActive || string.Equals(process, streakProcess, StringComparison.OrdinalIgnoreCase));

        if (continuesStreak)
        {
            streakEnd = end;
            continue;
        }

        FlushStreak();
        streakStart = start;
        streakEnd = end;
        streakActive = isActive;
        streakProcess = process;
    }

    FlushStreak();
    return segments;
}
```
Add `using ONEVO.Domain.Features.Monitoring.Meetings.Entities;` to the file's usings.

- [ ] **Step 5: Update `GetMyActivityTimelineQueryHandler.cs` to pass meeting signals through**

Read the handler first. It almost certainly already injects `IActivitySnapshotRepository` the same way `GetMyWorkPatternQueryHandler` does — add `IMeetingSignalRepository meetings` to its constructor (same repository interface `GetMyWorkPatternQueryHandler` already uses) and fetch+pass meeting signals into `BuildSegments`:

```csharp
var meetingSignals = await meetings.GetAllByEmployeeDateAsync(tenantId, employeeId, date, ct);
var segments = ActivityTimelineBuilder.BuildSegments(snaps, meetingSignals);
```

- [ ] **Step 6: Run backend tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "ActivityTimelineBuilderTests|GetMyActivityTimelineQueryHandlerTests" --nologo`
Expected: PASS.

- [ ] **Step 7: Update the frontend model and `timeline-segments.util.ts`**

Read `activity-timeline.model.ts` first (for the `ActivityTimelineSegment` type's exact current shape) and add `'meeting'` to its `type` union to match the backend's new `MeetingType`.

```typescript
// src/app/modules/dashboard/util/timeline-segments.util.ts
export interface TimelineSegment {
  readonly type: 'working' | 'break' | 'focus' | 'meeting' | 'idle';
  readonly widthPercent: number;
}

export const SEGMENT_LABELS: Record<TimelineSegment['type'], string> = {
  working: 'Working',
  break: 'Break',
  focus: 'Focus work',
  meeting: 'Meeting (est.)',
  idle: 'Idle time'
};

export const SEGMENT_COLORS: Record<TimelineSegment['type'], string> = {
  working: 'var(--color-accent)',
  break: 'var(--color-warning)',
  focus: '#6366f1',
  meeting: '#60a5fa',
  idle: 'var(--color-border)'
};

export const SEGMENT_OPACITY: Record<TimelineSegment['type'], number> = {
  working: 1,
  break: 1,
  focus: 1,
  meeting: 1,
  idle: 0.4
};
```

In `subdivideWorking`'s signature, widen the accepted activity type from `'focus' | 'idle'` to `'focus' | 'meeting' | 'idle'`:

```typescript
function subdivideWorking(
  rangeStart: number,
  rangeEnd: number,
  activity: (Interval & { type: 'focus' | 'meeting' | 'idle' })[]
): { type: TimelineSegment['type']; start: number; end: number }[] {
  // body unchanged - it's already generic over the activity interval's type field
```
And in `buildTimelineSegments`, widen `activity: ActivityTimelineSegment[]`'s consumption the same way (the `activityIntervals` mapping already passes `type: a.type` through generically — no other change needed there once the model's union type includes `'meeting'`).

- [ ] **Step 8: Write the failing frontend test, then verify it passes**

In `timeline-segments.util.spec.ts`, add a test mirroring the existing focus/idle subdivision tests but for a meeting interval — read the file's existing test structure first and match it exactly (same `buildTimelineSegments(start, end, breaks, activity)` call shape).

Run: `npm test -- --include='**/timeline-segments.util.spec.ts'`
Expected: PASS once the type widening lands (this is a pure type-safety change with one new test case, not new runtime branching logic — `subdivideWorking`'s existing loop already handles any `activity` interval generically).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityTimelineBuilder.cs src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetMyActivityTimeline/GetMyActivityTimelineQueryHandler.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityTimelineBuilderTests.cs
git commit -m "feat(monitoring): add a Meeting segment type to the activity timeline builder"
```
(Frontend files for this task get committed in the frontend repo separately:)
```bash
git add src/app/modules/dashboard/util/timeline-segments.util.ts src/app/modules/dashboard/util/timeline-segments.util.spec.ts src/app/modules/dashboard/models/activity-timeline.model.ts
git commit -m "feat(dashboard): render a Meeting segment on the Today's Activity timeline"
```

---

### Task 7: Live refresh — visibility-gated 60s polling on both dashboard cards

**Files:**
- Modify: `src/app/modules/dashboard/feature/today-at-a-glance-card/today-at-a-glance-card.component.ts`
- Modify: `src/app/modules/dashboard/feature/work-pattern-card/work-pattern-card.component.ts`
- Test: both components' `.spec.ts` files

**Interfaces:**
- Consumes: nothing new from earlier tasks.
- Produces: nothing consumed by later tasks — this is the last frontend task.

- [ ] **Step 1: Read both components' current `ngOnInit`/constructor lifecycle code in full**

Already partially known from this session's investigation (`today-at-a-glance-card`'s existing 60s `setInterval` only touches a `now` signal, doesn't refetch; `work-pattern-card.ngOnInit` calls `loadForPeriod` once). Read both files' current full lifecycle code before editing to get the exact current structure right.

- [ ] **Step 2: Write the failing tests**

```typescript
// Add to today-at-a-glance-card.component.spec.ts (match its existing TestBed/fakeAsync conventions)
it('refetches the timeline every 60s while the tab is visible', fakeAsync(() => {
  const loadSpy = spyOn(component['activityApi'], 'getMyTimeline').and.callThrough();
  fixture.detectChanges();
  const callsAfterInit = loadSpy.calls.count();

  tick(60_000);

  expect(loadSpy.calls.count()).toBeGreaterThan(callsAfterInit);
  discardPeriodicTasks();
}));

it('stops polling when the document becomes hidden, resumes and refetches immediately when visible again', fakeAsync(() => {
  const loadSpy = spyOn(component['activityApi'], 'getMyTimeline').and.callThrough();
  fixture.detectChanges();

  Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
  document.dispatchEvent(new Event('visibilitychange'));
  const callsWhileHidden = loadSpy.calls.count();
  tick(120_000); // well past 60s - must NOT have polled while hidden
  expect(loadSpy.calls.count()).toBe(callsWhileHidden);

  Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
  document.dispatchEvent(new Event('visibilitychange'));
  expect(loadSpy.calls.count()).toBeGreaterThan(callsWhileHidden); // immediate refetch on becoming visible

  discardPeriodicTasks();
}));
```
Write the matching pair of tests in `work-pattern-card.component.spec.ts` against `api.getMyWorkPattern`, but only when the currently-selected period includes today (`this-week`/`this-month`, the two defaults where "today" falls in range) — add a third test asserting NO polling happens when `selectedPeriod` is `'last-week'`/`'last-month'`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `npm test -- --include='**/today-at-a-glance-card.component.spec.ts'` and `--include='**/work-pattern-card.component.spec.ts'`
Expected: FAIL — no polling exists yet.

- [ ] **Step 4: Implement the polling in `today-at-a-glance-card.component.ts`**

```typescript
// Add near the existing constructor's 60s `now`-tick interval - extend it, don't add a second
// competing interval:
private pollTimer: ReturnType<typeof setInterval> | null = null;

constructor() {
  // ... existing now-tick interval and other constructor code stays ...
  this.startPolling();
  document.addEventListener('visibilitychange', this.onVisibilityChange);
}

ngOnDestroy(): void {
  this.stopPolling();
  document.removeEventListener('visibilitychange', this.onVisibilityChange);
}

private onVisibilityChange = (): void => {
  if (document.visibilityState === 'hidden') {
    this.stopPolling();
  } else {
    void this.refetchTimeline();
    this.startPolling();
  }
};

private startPolling(): void {
  if (this.pollTimer || document.visibilityState === 'hidden') return;
  this.pollTimer = setInterval(() => void this.refetchTimeline(), 60_000);
}

private stopPolling(): void {
  if (this.pollTimer) {
    clearInterval(this.pollTimer);
    this.pollTimer = null;
  }
}

private async refetchTimeline(): Promise<void> {
  if (!this.today()?.allowedClockInMethods?.desktopTray) return; // same gate ngOnInit already uses
  this.activityApi.getMyTimeline().subscribe({
    next: (segments) => this.timelineSegments.set(segments), // match whatever signal name the
                                                                // existing subscribe already sets
  });
}
```
Add `implements OnDestroy` to the component's class declaration if not already present; import `OnDestroy` from `@angular/core`. Match `activityApi`/`today`/whatever the existing injected service and signal names actually are — read the file first (Step 1) rather than guessing these identifiers.

- [ ] **Step 5: Implement the polling in `work-pattern-card.component.ts`**

```typescript
// Add:
private pollTimer: ReturnType<typeof setInterval> | null = null;

ngOnInit(): void {
  this.loadForPeriod(this.selectedPeriod());
  this.startPollingIfPeriodIncludesToday();
  document.addEventListener('visibilitychange', this.onVisibilityChange);
}

ngOnDestroy(): void {
  this.stopPolling();
  document.removeEventListener('visibilitychange', this.onVisibilityChange);
}

private onVisibilityChange = (): void => {
  if (document.visibilityState === 'hidden') {
    this.stopPolling();
  } else {
    this.loadForPeriod(this.selectedPeriod());
    this.startPollingIfPeriodIncludesToday();
  }
};

private periodIncludesToday(): boolean {
  const range = rangeForPeriod(this.selectedPeriod());
  const today = toDateOnly(new Date());
  return range.from <= today && today <= range.to;
}

private startPollingIfPeriodIncludesToday(): void {
  this.stopPolling();
  if (document.visibilityState === 'hidden' || !this.periodIncludesToday()) return;
  this.pollTimer = setInterval(() => this.loadForPeriod(this.selectedPeriod()), 60_000);
}

private stopPolling(): void {
  if (this.pollTimer) {
    clearInterval(this.pollTimer);
    this.pollTimer = null;
  }
}
```
Add `OnDestroy` to the class's `implements` clause and import it. In `onPeriodChange`, add a call to `this.startPollingIfPeriodIncludesToday();` after `this.loadForPeriod(period);` so switching to/from a today-including period correctly starts/stops the timer.

- [ ] **Step 6: Run tests to verify they pass**

Run: `npm test -- --include='**/today-at-a-glance-card.component.spec.ts'` and `--include='**/work-pattern-card.component.spec.ts'`
Expected: PASS.

- [ ] **Step 7: Full frontend suite**

Run: `npm test -- --watch=false`
Expected: no new failures beyond the known pre-existing baseline (4 unrelated failures — work.routes, notification-bell, member-management-popup).

- [ ] **Step 8: Commit**

```bash
git add src/app/modules/dashboard/feature/today-at-a-glance-card/ src/app/modules/dashboard/feature/work-pattern-card/
git commit -m "feat(dashboard): poll live today metrics every 60s while the tab is visible, pause when hidden"
```

---

### Task 8: Final full-suite verification, both repos

- [ ] **Step 1: Backend**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo
```
Expected: 0 errors, full suite green (this now includes every test from Tasks 1-3 and 6's backend portion).

- [ ] **Step 2: Frontend**

```bash
npm test -- --watch=false
```
Expected: no new failures beyond the known 4-failure pre-existing baseline.

- [ ] **Step 3: Manual smoke check (if a dev server is available)**

Load the Employee Dashboard, confirm: the Work Pattern card shows "Other active" (not "Admin / Other"), the Productive tile shows a real percentage with "X% of active time" beneath it, the Observed Coverage caption appears, the distribution bar visually fills the container's full width (no unexplained blank gap), the Meeting tile/tooltip says "(est.)" / shows the estimate disclaimer, and the Today's Activity timeline shows a distinct meeting-colored segment when applicable.

- [ ] **Step 4: Report**

Summarize what changed, both repos' final test counts, and flag the past-day `ActivityDailySummary.FocusMinutes`/`ProductiveAppMinutes` values will shift slightly on the next nightly run (Task 2's noted behavior correction) — this is expected, not a regression.

---

## Self-Review

**Spec coverage:** every element of the frozen model (Steps 1-3 of the algorithm, the 8-hour worked example, all 6 card names, the bar's exact-100% construction, the Meeting "(est.)" labeling, the visibility-gated 60s refresh, the live-vs-nightly consistency guarantee) has a corresponding task. The `AppCategoryClassifier` Infrastructure→Application move was not explicitly requested by the user but is a structural necessity the frozen model's own dependency (`WorkPatternWindowClassifier` needs `AppCategoryClassifier`, and Application cannot depend on Infrastructure) forces — flagged inline in Task 1 rather than silently done.

**Placeholder scan:** no TBD/TODO/"add error handling" patterns — every step has real code or a concrete "read file X first, then do Y" instruction where the exact current file content genuinely can't be pre-derived without reading it fresh (Task 4 Step 1, Task 5 Step 1, Task 6 Step 1, Task 7 Step 1) — these are honest acknowledgments that some files weren't fully read this session, not placeholders for logic.

**Type consistency:** `WorkPatternTotals` (Task 1) → consumed identically by Task 2 (`ActivityDailySummaryAggregator`) and Task 3 (`GetMyWorkPatternQueryHandler`) → `WorkPatternDayDto` (Task 3) → `WorkPatternDay` (Task 4, frontend) → `work-pattern-card.component.ts` (Task 5). Field names (`FocusMinutes`, `MeetingMinutes`, `OtherActiveMinutes`, `IdleMinutes`, `ProductiveMinutes` / `focusMinutes`, `meetingMinutes`, `otherActiveMinutes`, `idleMinutes`, `productiveMinutes`) are consistent end-to-end across every task that touches them.
