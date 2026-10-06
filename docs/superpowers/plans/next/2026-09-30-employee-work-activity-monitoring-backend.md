# Employee Work & Activity — Agent Cards (Plan 4B, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the tray-agent endpoints for the Work & Activity tab:
- `work-activity/activity-by-hour`: the hourly chart and last activity
- `work-activity/app-usage`: Application usage
- `work-activity/work-pattern`: focus blocks, top app and longest idle

It also makes Plan 3B's `overview/activity` include today.

**Architecture:**
- New range reads on the snapshot repositories. Aggregation happens in SQL where it can (per-slot sums, per-process counts); raw rows are read only where the rule needs an order (sessions, focus streaks, idle runs).
- A pure `EmployeeWorkActivityCalculator` shapes the rows.
- Three thin handlers: guard → `monitoring:read`-or-self → period → capability toggle → shape.

**Tech Stack:** ASP.NET Core, MediatR, EF Core (Npgsql; InMemory in unit tests), xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md` plus the Work & Activity mockup. Sibling plans: 4A (`2026-09-30-employee-work-activity-tasks-backend.md`), frontend `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-30-employee-work-activity-tab.md`.

## Prerequisites

- **Plan 3B** (`../2026-09-30-employee-overview-activity-discipline-backend.md`) Task 1 is merged: `overview/activity` exists. Task 6 of this plan changes its handler.
- Plan 2 is merged (`EmployeePeriod`, `EmployeeOverviewAccess`, the architecture Theory).

## Global Constraints

- **Branch:** same as 4A, `feature/employee-work-activity`, in a clean checkout (not `C:\HR2\HRMS-Backend-v1`, which has unrelated uncommitted changes). Before each commit, run `git branch --show-current` and `git status --short`, and stage only the task's files.
- **Before each `dotnet test`:** `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue`.
- **Gate:** the route has `[RequirePermission("employees:read")]`, and the handler runs `IEmployeeReadAccessGuard.EnsureCanRead` and then `EmployeeOverviewAccess.HasAccessAsync(..., "monitoring:read", ...)`: `monitoring:read` or viewing your own record.
- **Capability toggles** (`IMonitoringToggleResolver.IsEnabledAsync(tenantId, employeeId, capability)`):
  - `activity-by-hour` and `work-pattern` use `ActivityMonitoring`.
  - `app-usage` uses `ApplicationTracking`. `work-pattern`'s top app also needs `ApplicationTracking`; when it is off, `topApp` is `null`.
  - When the capability is off, the response has `…Enabled: false` and empty or zero data, and **no snapshot rows are read**.
- **Period:** `EmployeePeriod.Resolve(from, to, today)` (default = current month), and then these raw-row endpoints also reject more than **31 days** with 400 (`EmployeeWorkActivityCalculator.MaxRawDays`).
- **Day windows:**
  - `app-usage` and `work-pattern` use UTC days `[From 00:00Z, To+1 00:00Z)`, the same as every other monitoring read.
  - **Documented exception:** `activity-by-hour` buckets by the employee's **legal-entity timezone** because the chart is labelled in local hours (9AM–5PM). Its window is `[local From 00:00, local To+1 00:00)` via `AttendanceTodayStateService.GetLocalDayWindow`, and the timezone comes from `AttendancePeriodCalculator.ResolveTimezone(legalEntity.Timezone)`, with UTC as the fallback. SQL sums active seconds per **UTC half-hour slot**, and each slot is moved to its local hour in memory. This is exact for whole- and half-hour offsets; for 45-minute offsets (e.g. Nepal) a slot lands in the local hour where it starts.
- **Sampling facts from the agent:**
  - An `AppUsageSnapshot` is one 60s sample, so 1 row = 1 minute (`ActivityDailySummaryAggregator.AppUsageMinutesPerSample`).
  - An `ActivitySnapshot` window is `[CapturedAt - (ActiveSeconds + IdleSeconds), CapturedAt]`.
- **Metric rules:**
  - **App session:** a run of one process's samples where each gap to the previous sample of the *same* process is ≤ 2 minutes. Any switch to another app leaves a gap of at least 2 minutes, which ends the run.
  - **Focus block:** a 30+ minute same-process active streak, as defined by `WorkPatternWindowClassifier`. It is counted per UTC day and summed. `ActivityDailySummary.DeepFocusSessionsCount` is 0 or 1 per day, so it is **not** a count and must not be used.
  - **Longest idle:** the longest run of consecutive windows with `ActiveSeconds == 0 && IdleSeconds > 0`, where each window starts within 90s of the previous window's end.
- **Performance:** p95 ≤ 400ms. `(tenant_id, employee_id, captured_at)` indexes already exist on `activity_snapshots`, `app_usage_snapshots` and `meeting_signals`. Every read is `AsNoTracking` and projected to the needed columns. Task 5 ends with a timing check on seeded data.
- Existing endpoints and repository methods stay unchanged.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/WorkPatternWindowClassifier.cs` (modify) | Add `FocusBlocks` to `WorkPatternTotals` |
| `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/RepositoryInterfaces/IActivitySnapshotRepository.cs` (modify) | `ActivitySlotRow` + 3 range reads |
| `src/ONEVO.Application/Features/Monitoring/AppUsage/RepositoryInterfaces/IAppUsageSnapshotRepository.cs` (modify) | `AppProcessMinutesRow`, `AppProcessSampleRow` + 2 range reads |
| `src/ONEVO.Application/Features/Monitoring/Meetings/RepositoryInterfaces/IMeetingSignalRepository.cs` (modify) | 1 range read |
| `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/{ActivityMonitoring,AppUsage,Meetings}/Ef*Repository.cs` (modify) | EF implementations |
| `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeWorkActivityResponses.cs` (create) | Response DTOs |
| `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/EmployeeWorkActivityCalculator.cs` (create) | Pure shaping |
| `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeWorkActivity/{GetEmployeeActivityByHour,GetEmployeeAppUsage,GetEmployeeWorkPattern}/*` (create) | Queries + handlers |
| `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (modify) | 3 actions |
| Tests under `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/` and `tests/ONEVO.Tests.Architecture/` | See each task |

---

### Task 1: Count focus blocks in the classifier

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/WorkPatternWindowClassifier.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternWindowClassifierTests.cs`

- [ ] **Step 1: Write the failing test** (append to `WorkPatternWindowClassifierTests`, which already has the `Snap(...)` helper)

```csharp
    [Fact]
    public void Classify_CountsEachThirtyMinuteSameProcessStreakAsOneFocusBlock()
    {
        var t = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = new List<ActivitySnapshot>();
        for (var i = 1; i <= 31; i++) snaps.Add(Snap(t.AddMinutes(i), 60, 0, "code.exe"));        // block 1 (31 min)
        for (var i = 32; i <= 41; i++) snaps.Add(Snap(t.AddMinutes(i), 60, 0, "chrome.exe"));     // 10 min, not a block
        for (var i = 42; i <= 73; i++) snaps.Add(Snap(t.AddMinutes(i), 60, 0, "code.exe"));       // block 2 (32 min)

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        Assert.Equal(2, result.FocusBlocks);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkPatternWindowClassifierTests"`
Expected: build FAIL, because `WorkPatternTotals` has no `FocusBlocks`.

- [ ] **Step 3: Implement**

Change the record to add a trailing optional member, so no existing construction breaks:

```csharp
public sealed record WorkPatternTotals(
    int FocusMinutes, int OtherActiveMinutes, int IdleMinutes, int MeetingBarMinutes,
    int ProductiveFocusMinutes, int ProductiveOtherActiveMinutes, int FocusBlocks = 0);
```

In `Classify`, declare `var focusBlocks = 0;` next to the other accumulators. Inside `FlushStreak`'s `if (minutes >= FocusThresholdMinutes)` branch, add `focusBlocks++;` as the first line. In the final `return new WorkPatternTotals(...)`, add `FocusBlocks: focusBlocks` after `ProductiveOtherActiveMinutes: ...`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkPattern"`
Expected: PASS, including `WorkPatternLiveVsNightlyConsistencyTests` and `WorkPatternInvariantTests` (the totals are unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/WorkPatternWindowClassifier.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/WorkPatternWindowClassifierTests.cs
git commit -m "feat(monitoring): count focus blocks in the work pattern classifier

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Range reads on the snapshot repositories

**Files:**
- Modify: `IActivitySnapshotRepository.cs`, `IAppUsageSnapshotRepository.cs`, `IMeetingSignalRepository.cs` (Application)
- Modify: `EfActivitySnapshotRepository.cs`, `EfAppUsageSnapshotRepository.cs`, `EfMeetingSignalRepository.cs` (Infrastructure)
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/EmployeeWorkActivityRepositoryReadsTests.cs`

**Produces:** `ActivitySlotRow`, `AppProcessMinutesRow`, `AppProcessSampleRow`, and the methods listed in Step 3. Tasks 3–5 use them.

- [ ] **Step 1: Write the failing tests**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class EmployeeWorkActivityRepositoryReadsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

    private static ActivitySnapshot Snap(DateTimeOffset at, int active, int idle = 0, Guid? employeeId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId ?? EmployeeId, AgentDeviceId = Guid.NewGuid(),
        CapturedAt = at, ActiveSeconds = active, IdleSeconds = idle, ForegroundProcessName = "code.exe", CreatedAt = at
    };

    private static AppUsageSnapshot App(DateTimeOffset at, string process) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
        CapturedAt = at, ProcessName = process, CreatedAt = at
    };

    [Fact]
    public async Task ActiveSecondsByHalfHour_SumsPerUtcHalfHourSlot_InsideTheWindowOnly()
    {
        await using var db = BuildDb();
        db.ActivitySnapshots.AddRange(
            Snap(From.AddMinutes(10), 60), Snap(From.AddMinutes(20), 30),   // slot 00:00
            Snap(From.AddMinutes(40), 45),                                   // slot 00:30
            Snap(To.AddMinutes(5), 60),                                      // outside
            Snap(From.AddMinutes(10), 60, employeeId: Guid.NewGuid()));      // other employee
        await db.SaveChangesAsync();

        var rows = await new EfActivitySnapshotRepository(db).GetActiveSecondsByHalfHourAsync(TenantId, EmployeeId, From, To, default);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SlotStartUtc == From && r.ActiveSeconds == 90);
        Assert.Contains(rows, r => r.SlotStartUtc == From.AddMinutes(30) && r.ActiveSeconds == 45);
    }

    [Fact]
    public async Task LastActiveAt_IsTheLatestSnapshotWithActiveSeconds()
    {
        await using var db = BuildDb();
        db.ActivitySnapshots.AddRange(Snap(From.AddHours(3), 60), Snap(From.AddHours(4), 0, idle: 60));
        await db.SaveChangesAsync();

        var last = await new EfActivitySnapshotRepository(db).GetLastActiveAtAsync(TenantId, EmployeeId, From, To, default);

        Assert.Equal(From.AddHours(3), last);
    }

    [Fact]
    public async Task WindowsByEmployeeRange_ReturnsOrderedWindowsInRange()
    {
        await using var db = BuildDb();
        db.ActivitySnapshots.AddRange(Snap(From.AddHours(2), 60), Snap(From.AddHours(1), 60), Snap(To, 60));
        await db.SaveChangesAsync();

        var rows = await new EfActivitySnapshotRepository(db).GetWindowsByEmployeeRangeAsync(TenantId, EmployeeId, From, To, default);

        Assert.Equal(new[] { From.AddHours(1), From.AddHours(2) }, rows.Select(r => r.CapturedAt));
    }

    [Fact]
    public async Task AppMinutesByProcess_CountsSamplesAndLastSeen_PerProcess()
    {
        await using var db = BuildDb();
        db.AppUsageSnapshots.AddRange(
            App(From.AddMinutes(1), "code.exe"), App(From.AddMinutes(2), "code.exe"),
            App(From.AddMinutes(3), "chrome.exe"), App(To.AddMinutes(1), "code.exe"));
        await db.SaveChangesAsync();

        var rows = await new EfAppUsageSnapshotRepository(db).GetMinutesByProcessAsync(TenantId, EmployeeId, From, To, default);

        var code = Assert.Single(rows, r => r.ProcessName == "code.exe");
        Assert.Equal(2, code.Samples);
        Assert.Equal(From.AddMinutes(2), code.LastCapturedAt);
    }

    [Fact]
    public async Task AppSamplesForProcesses_ReturnsOnlyRequestedProcesses_Ordered()
    {
        await using var db = BuildDb();
        db.AppUsageSnapshots.AddRange(
            App(From.AddMinutes(2), "code.exe"), App(From.AddMinutes(1), "code.exe"), App(From.AddMinutes(3), "chrome.exe"));
        await db.SaveChangesAsync();

        var rows = await new EfAppUsageSnapshotRepository(db)
            .GetSamplesForProcessesAsync(TenantId, EmployeeId, From, To, new[] { "code.exe" }, default);

        Assert.Equal(new[] { From.AddMinutes(1), From.AddMinutes(2) }, rows.Select(r => r.CapturedAt));
    }

    [Fact]
    public async Task MeetingSignalsByRange_ReturnsSignalsInRange()
    {
        await using var db = BuildDb();
        db.MeetingSignals.AddRange(
            new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = From.AddHours(1), IsMeetingAppRunning = true },
            new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = To, IsMeetingAppRunning = true });
        await db.SaveChangesAsync();

        var rows = await new EfMeetingSignalRepository(db).GetByEmployeeRangeAsync(TenantId, EmployeeId, From, To, default);

        Assert.Single(rows);
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkActivityRepositoryReadsTests"`
Expected: build FAIL, because the methods do not exist.

- [ ] **Step 3: Add interface members**

`IActivitySnapshotRepository.cs`: add this record above the interface:

```csharp
/// <summary>Sum of ActiveSeconds of one employee's snapshots captured in one UTC half-hour slot.</summary>
public sealed record ActivitySlotRow(DateTimeOffset SlotStartUtc, int ActiveSeconds);
```

and these members inside the interface:

```csharp
    /// <summary>ActiveSeconds summed per UTC half-hour slot (by CapturedAt) in [fromUtc, toUtcExclusive).</summary>
    Task<IReadOnlyList<ActivitySlotRow>> GetActiveSecondsByHalfHourAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    /// <summary>Latest CapturedAt with ActiveSeconds > 0 in the window, or null.</summary>
    Task<DateTimeOffset?> GetLastActiveAtAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    /// <summary>Snapshots in the window ordered by CapturedAt, projected to CapturedAt, ActiveSeconds,
    /// IdleSeconds and ForegroundProcessName (the fields WorkPatternWindowClassifier reads).</summary>
    Task<IReadOnlyList<ActivitySnapshot>> GetWindowsByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);
```

`IAppUsageSnapshotRepository.cs`: add these records above the interface:

```csharp
/// <summary>Samples (= minutes, one per 60s) and last sample time of one process in a window.</summary>
public sealed record AppProcessMinutesRow(string ProcessName, int Samples, DateTimeOffset LastCapturedAt);

public sealed record AppProcessSampleRow(string ProcessName, DateTimeOffset CapturedAt);
```

and these members:

```csharp
    Task<IReadOnlyList<AppProcessMinutesRow>> GetMinutesByProcessAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    Task<IReadOnlyList<AppProcessSampleRow>> GetSamplesForProcessesAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive,
        IReadOnlyCollection<string> processNames, CancellationToken ct);
```

`IMeetingSignalRepository.cs`:

```csharp
    Task<IReadOnlyList<MeetingSignal>> GetByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);
```

- [ ] **Step 4: Implement**

`EfActivitySnapshotRepository`:

```csharp
    public async Task<IReadOnlyList<ActivitySlotRow>> GetActiveSecondsByHalfHourAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
    {
        // Grouped in SQL (at most 48 slots/day) - never loads the raw rows.
        var slots = await _db.ActivitySnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .GroupBy(s => new { s.CapturedAt.Year, s.CapturedAt.Month, s.CapturedAt.Day, s.CapturedAt.Hour, Half = s.CapturedAt.Minute / 30 })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, g.Key.Half, Active = g.Sum(s => s.ActiveSeconds) })
            .ToListAsync(ct);

        return slots
            .Select(s => new ActivitySlotRow(new DateTimeOffset(s.Year, s.Month, s.Day, s.Hour, s.Half * 30, 0, TimeSpan.Zero), s.Active))
            .ToList();
    }

    public async Task<DateTimeOffset?> GetLastActiveAtAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.ActivitySnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ActiveSeconds > 0
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .OrderByDescending(s => s.CapturedAt)
            .Select(s => (DateTimeOffset?)s.CapturedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ActivitySnapshot>> GetWindowsByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.ActivitySnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new ActivitySnapshot
            {
                TenantId = s.TenantId, EmployeeId = s.EmployeeId, CapturedAt = s.CapturedAt,
                ActiveSeconds = s.ActiveSeconds, IdleSeconds = s.IdleSeconds, ForegroundProcessName = s.ForegroundProcessName
            })
            .ToListAsync(ct);
```

`EfAppUsageSnapshotRepository`:

```csharp
    public async Task<IReadOnlyList<AppProcessMinutesRow>> GetMinutesByProcessAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.AppUsageSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ProcessName != null
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .GroupBy(s => s.ProcessName!)
            .Select(g => new AppProcessMinutesRow(g.Key, g.Count(), g.Max(s => s.CapturedAt)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AppProcessSampleRow>> GetSamplesForProcessesAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive,
        IReadOnlyCollection<string> processNames, CancellationToken ct)
        => await _db.AppUsageSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ProcessName != null
                        && processNames.Contains(s.ProcessName)
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new AppProcessSampleRow(s.ProcessName!, s.CapturedAt))
            .ToListAsync(ct);
```

`EfMeetingSignalRepository`:

```csharp
    public async Task<IReadOnlyList<MeetingSignal>> GetByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.MeetingSignals.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .ToListAsync(ct);
```

If another class implements these interfaces (test fakes), add the same members there. Find them with `grep -rn ": IActivitySnapshotRepository\|: IAppUsageSnapshotRepository\|: IMeetingSignalRepository" src tests`.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkActivityRepositoryReadsTests"`
Expected: PASS (6 tests).

- [ ] **Step 5a: Check that PostgreSQL translates the queries (do not skip)**

The InMemory tests above evaluate LINQ in memory, so they cannot catch Npgsql translation failures. Three queries are risky:
- the `GroupBy` on `CapturedAt.Year/Month/Day/Hour/Minute / 30`
- the `Select(s => new ActivitySnapshot { ... })` entity projection
- the `GroupBy(...).Select(g => new AppProcessMinutesRow(g.Key, g.Count(), g.Max(...)))`

Exercise each method once against real PostgreSQL:
- **With Docker running:** add an `ONEVO.Tests.Integration` test per method, following an existing repository test there.
- **Without Docker:** write a throwaway scratch test that builds `ApplicationDbContext` with `UseNpgsql(<dev connection string from appsettings.Development.json>)` and calls the six methods for a real employee. Run it once, then delete the file without committing it.

Fallbacks if a query fails to translate:
- **Half-hour `GroupBy`:** project `.Select(s => new { s.CapturedAt, s.ActiveSeconds })`, `ToListAsync`, then bucket in memory with the same slot rule: `new DateTimeOffset(y, m, d, h, (minute / 30) * 30, 0, TimeSpan.Zero)` on `CapturedAt.ToUniversalTime()`. At the 31-day cap this is at most about 45k two-column rows.
- **Entity projection:** project to an anonymous type, then map to `new ActivitySnapshot { ... }` after `ToListAsync`.
- **Record constructor in `GroupBy`:** select an anonymous type `{ Process = g.Key, Samples = g.Count(), Last = g.Max(s => s.CapturedAt) }`, then map to `AppProcessMinutesRow` after `ToListAsync`.

Keep the InMemory tests either way; they pin the behaviour.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/EmployeeWorkActivityRepositoryReadsTests.cs
git commit -m "feat(monitoring): range reads for employee work & activity cards

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: DTOs and `EmployeeWorkActivityCalculator`

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeWorkActivityResponses.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/EmployeeWorkActivityCalculator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/EmployeeWorkActivityCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class EmployeeWorkActivityCalculatorTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("c", TimeSpan.FromHours(5.5), "c", "c");
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HourlyActiveMinutes_MovesUtcHalfHourSlotsToLocalHours_AndReturns24Buckets()
    {
        var slots = new[]
        {
            new ActivitySlotRow(Day.AddHours(3).AddMinutes(30), 1800), // 03:30Z = 09:00 local
            new ActivitySlotRow(Day.AddHours(4), 600)                  // 04:00Z = 09:30 local
        };

        var hours = EmployeeWorkActivityCalculator.HourlyActiveMinutes(slots, Colombo);

        hours.Should().HaveCount(24);
        hours[9].ActiveMinutes.Should().Be(40);
        hours.Where(h => h.Hour != 9).Sum(h => h.ActiveMinutes).Should().Be(0);
    }

    [Fact]
    public void AppUsage_TakesTopByMinutes_AndCountsSessionsSplitByGapsOverTwoMinutes()
    {
        var totals = new[]
        {
            new AppProcessMinutesRow("code.exe", 5, Day.AddMinutes(20)),
            new AppProcessMinutesRow("chrome.exe", 2, Day.AddMinutes(8)),
            new AppProcessMinutesRow("slack.exe", 1, Day.AddMinutes(30))
        };
        var samples = new[]
        {
            new AppProcessSampleRow("code.exe", Day.AddMinutes(1)), new AppProcessSampleRow("code.exe", Day.AddMinutes(2)),
            new AppProcessSampleRow("code.exe", Day.AddMinutes(3)),                 // session 1
            new AppProcessSampleRow("code.exe", Day.AddMinutes(19)), new AppProcessSampleRow("code.exe", Day.AddMinutes(20)), // session 2
            new AppProcessSampleRow("chrome.exe", Day.AddMinutes(7)), new AppProcessSampleRow("chrome.exe", Day.AddMinutes(8))
        };

        var apps = EmployeeWorkActivityCalculator.AppUsage(totals, samples, take: 2);

        apps.Select(a => a.AppName).Should().Equal("code.exe", "chrome.exe");
        apps[0].ActiveMinutes.Should().Be(5);
        apps[0].Sessions.Should().Be(2);
        apps[0].LastUsedAt.Should().Be(Day.AddMinutes(20));
        apps[1].Sessions.Should().Be(1);
    }

    [Fact]
    public void LongestIdle_FindsTheLongestContiguousIdleRun()
    {
        ActivitySnapshot W(int minute, int active, int idle) => new() { CapturedAt = Day.AddMinutes(minute), ActiveSeconds = active, IdleSeconds = idle };
        var windows = new[]
        {
            W(1, 60, 0), W(2, 0, 60), W(3, 0, 60),            // idle 01:01-01:03 (2 min)
            W(4, 60, 0),
            W(5, 0, 60), W(6, 0, 60), W(7, 0, 60), W(8, 0, 60) // idle 00:04-00:08 (4 min)
        };

        var idle = EmployeeWorkActivityCalculator.LongestIdle(windows);

        idle!.Start.Should().Be(Day.AddMinutes(4));
        idle.End.Should().Be(Day.AddMinutes(8));
        idle.Minutes.Should().Be(4);
    }

    [Fact]
    public void LongestIdle_OfNoIdleWindows_IsNull()
    {
        EmployeeWorkActivityCalculator.LongestIdle(Array.Empty<ActivitySnapshot>()).Should().BeNull();
    }

    [Fact]
    public void UtcWindow_CoversWholeUtcDays()
    {
        var (from, to) = EmployeeWorkActivityCalculator.UtcWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        from.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        to.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkActivityCalculatorTests"`
Expected: build FAIL.

- [ ] **Step 3: Create the DTOs**

```csharp
namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record ActivityHourBucket(int Hour, int ActiveMinutes);

/// <summary>Hours are 0..23 in Timezone (the employee's legal-entity zone, IANA/Windows id or "UTC").</summary>
public sealed record EmployeeActivityByHourResponse(
    DateOnly From,
    DateOnly To,
    bool ActivityMonitoringEnabled,
    string Timezone,
    DateTimeOffset? LastActivityAt,
    IReadOnlyList<ActivityHourBucket> Hours);

public sealed record EmployeeAppUsageItem(string AppName, int ActiveMinutes, int Sessions, DateTimeOffset LastUsedAt);

public sealed record EmployeeAppUsageResponse(
    DateOnly From,
    DateOnly To,
    bool ApplicationTrackingEnabled,
    int TotalMinutes,
    IReadOnlyList<EmployeeAppUsageItem> Apps);

public sealed record EmployeeIdlePeriod(DateTimeOffset Start, DateTimeOffset End, int Minutes);

public sealed record EmployeeWorkPatternSummaryResponse(
    DateOnly From,
    DateOnly To,
    bool ActivityMonitoringEnabled,
    int FocusBlocks,
    string? TopApp,
    int TopAppMinutes,
    EmployeeIdlePeriod? LongestIdle);
```

- [ ] **Step 4: Create the calculator**

```csharp
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

/// <summary>Pure shaping for the employee Work & Activity agent cards (Plan 4B).</summary>
public static class EmployeeWorkActivityCalculator
{
    public const int MaxRawDays = 31;
    public const int TopApps = 5;
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleContinuityTolerance = TimeSpan.FromSeconds(90);

    public static (DateTimeOffset From, DateTimeOffset To) UtcWindow(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
         new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    public static bool ExceedsRawRange(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber + 1 > MaxRawDays;

    public static IReadOnlyList<ActivityHourBucket> HourlyActiveMinutes(IReadOnlyList<ActivitySlotRow> slots, TimeZoneInfo zone)
    {
        var seconds = new long[24];
        foreach (var slot in slots)
            seconds[TimeZoneInfo.ConvertTime(slot.SlotStartUtc, zone).Hour] += slot.ActiveSeconds;
        return Enumerable.Range(0, 24).Select(h => new ActivityHourBucket(h, (int)(seconds[h] / 60))).ToList();
    }

    public static IReadOnlyList<EmployeeAppUsageItem> AppUsage(
        IReadOnlyList<AppProcessMinutesRow> totals, IReadOnlyList<AppProcessSampleRow> samples, int take)
    {
        var sessions = samples
            .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => CountSessions(g.Select(s => s.CapturedAt)), StringComparer.OrdinalIgnoreCase);

        return totals
            .OrderByDescending(t => t.Samples)
            .ThenBy(t => t.ProcessName)
            .Take(take)
            .Select(t => new EmployeeAppUsageItem(
                t.ProcessName, t.Samples, sessions.TryGetValue(t.ProcessName, out var n) ? n : 0, t.LastCapturedAt))
            .ToList();
    }

    public static IReadOnlyList<string> TopProcessNames(IReadOnlyList<AppProcessMinutesRow> totals, int take) =>
        totals.OrderByDescending(t => t.Samples).ThenBy(t => t.ProcessName).Take(take).Select(t => t.ProcessName).ToList();

    public static EmployeeIdlePeriod? LongestIdle(IReadOnlyList<ActivitySnapshot> windows)
    {
        EmployeeIdlePeriod? best = null;
        DateTimeOffset? runStart = null, runEnd = null;

        foreach (var w in windows.OrderBy(w => w.CapturedAt))
        {
            var start = w.CapturedAt - TimeSpan.FromSeconds(w.ActiveSeconds + w.IdleSeconds);
            var isIdle = w.ActiveSeconds == 0 && w.IdleSeconds > 0;

            if (isIdle && runEnd is not null && start <= runEnd.Value + IdleContinuityTolerance)
            {
                runEnd = w.CapturedAt;
            }
            else if (isIdle)
            {
                runStart = start;
                runEnd = w.CapturedAt;
            }
            else
            {
                runStart = runEnd = null;
            }

            if (runStart is not null && runEnd is not null)
            {
                var minutes = (int)(runEnd.Value - runStart.Value).TotalMinutes;
                if (best is null || minutes > best.Minutes)
                    best = new EmployeeIdlePeriod(runStart.Value, runEnd.Value, minutes);
            }
        }
        return best;
    }

    private static int CountSessions(IEnumerable<DateTimeOffset> capturedAt)
    {
        var count = 0;
        DateTimeOffset? previous = null;
        foreach (var at in capturedAt.OrderBy(a => a))
        {
            if (previous is null || at - previous.Value > SessionGap)
                count++;
            previous = at;
        }
        return count;
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkActivityCalculatorTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeWorkActivityResponses.cs src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/EmployeeWorkActivityCalculator.cs tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/EmployeeWorkActivityCalculatorTests.cs
git commit -m "feat(monitoring): work & activity DTOs and calculator

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The three query handlers

**Files** (all under `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeWorkActivity/`):
- Create: `GetEmployeeActivityByHour/GetEmployeeActivityByHourQuery.cs` + `...Handler.cs`
- Create: `GetEmployeeAppUsage/GetEmployeeAppUsageQuery.cs` + `...Handler.cs`
- Create: `GetEmployeeWorkPattern/GetEmployeeWorkPatternQuery.cs` + `...Handler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/GetEmployeeWorkActivityHandlersTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeAppUsage;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeWorkPattern;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.LegalEntity.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class GetEmployeeWorkActivityHandlersTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IActivitySnapshotRepository> _snapshots = new();
    private readonly Mock<IAppUsageSnapshotRepository> _apps = new();
    private readonly Mock<IMeetingSignalRepository> _meetings = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeWorkActivityHandlersTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _apps.Setup(a => a.GetMinutesByProcessAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppProcessMinutesRow("code.exe", 72, DateTimeOffset.Parse("2026-09-15T09:05:00+00:00")) });
        _apps.Setup(a => a.GetSamplesForProcessesAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppProcessSampleRow>());
        _snapshots.Setup(s => s.GetWindowsByEmployeeRangeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActivitySnapshot>());
        _meetings.Setup(m => m.GetByEmployeeRangeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MeetingSignal>());
    }

    private GetEmployeeActivityByHourQueryHandler ByHour() =>
        new(_guard.Object, _employees.Object, _snapshots.Object, _legalEntities.Object, _toggles.Object, _user.Object, _clock.Object);
    private GetEmployeeAppUsageQueryHandler Apps() =>
        new(_guard.Object, _employees.Object, _apps.Object, _toggles.Object, _user.Object, _clock.Object);
    private GetEmployeeWorkPatternQueryHandler Pattern() =>
        new(_guard.Object, _employees.Object, _snapshots.Object, _meetings.Object, _apps.Object, _toggles.Object, _user.Object, _clock.Object);

    [Fact]
    public async Task ByHour_WhenActivityMonitoringIsOff_ReturnsDisabled_WithoutReadingSnapshots()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await ByHour().Handle(new GetEmployeeActivityByHourQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.ActivityMonitoringEnabled.Should().BeFalse();
        result.Value.Hours.Should().BeEmpty();
        _snapshots.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ByHour_Rejects_MoreThan31Days()
    {
        var result = await ByHour().Handle(
            new GetEmployeeActivityByHourQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ByHour_ReturnsTwentyFourUtcHours_WhenTheEmployeeHasNoLegalEntity()
    {
        _snapshots.Setup(s => s.GetActiveSecondsByHalfHourAsync(_tenantId, _employeeId,
                DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ActivitySlotRow(DateTimeOffset.Parse("2026-09-02T09:00:00+00:00"), 1200) });

        var result = await ByHour().Handle(new GetEmployeeActivityByHourQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.Timezone.Should().Be("UTC");
        result.Value.Hours.Should().HaveCount(24);
        result.Value.Hours[9].ActiveMinutes.Should().Be(20);
    }

    [Fact]
    public async Task Apps_Forbidden_WithoutMonitoringReadWhenNotSelf()
    {
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(false);

        var result = await Apps().Handle(new GetEmployeeAppUsageQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Apps_ReturnsTopAppsWithTotalMinutes()
    {
        var result = await Apps().Handle(new GetEmployeeAppUsageQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.ApplicationTrackingEnabled.Should().BeTrue();
        result.Value.TotalMinutes.Should().Be(72);
        result.Value.Apps.Should().ContainSingle().Which.AppName.Should().Be("code.exe");
    }

    [Fact]
    public async Task Pattern_OmitsTopApp_WhenApplicationTrackingIsOff()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Pattern().Handle(new GetEmployeeWorkPatternQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.ActivityMonitoringEnabled.Should().BeTrue();
        result.Value.TopApp.Should().BeNull();
        _apps.VerifyNoOtherCalls();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkActivityHandlersTests"`
Expected: build FAIL.

- [ ] **Step 3: Create the queries**

```csharp
// GetEmployeeActivityByHourQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;

public sealed record GetEmployeeActivityByHourQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeActivityByHourResponse>>;
```

```csharp
// GetEmployeeAppUsageQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeAppUsage;

public sealed record GetEmployeeAppUsageQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeAppUsageResponse>>;
```

```csharp
// GetEmployeeWorkPatternQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeWorkPattern;

public sealed record GetEmployeeWorkPatternQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeWorkPatternSummaryResponse>>;
```

- [ ] **Step 4: Create the handlers**

`GetEmployeeActivityByHourQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.OrgStructure.LegalEntity.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;

/// <summary>Active minutes per local hour of day, summed over the period (legal-entity timezone -
/// the one documented exception to UTC days in Plan 4B), plus the last active snapshot time.</summary>
public sealed class GetEmployeeActivityByHourQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IActivitySnapshotRepository snapshots,
    ILegalEntityRepository legalEntities,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeActivityByHourQuery, Result<EmployeeActivityByHourResponse>>
{
    public const string ModulePermission = "monitoring:read";

    public async Task<Result<EmployeeActivityByHourResponse>> Handle(GetEmployeeActivityByHourQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeActivityByHourResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeActivityByHourResponse>.Forbidden("You do not have access to this employee's activity.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeActivityByHourResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var p = period.Value!;
        if (EmployeeWorkActivityCalculator.ExceedsRawRange(p.From, p.To))
            return Result<EmployeeActivityByHourResponse>.Failure($"The period cannot exceed {EmployeeWorkActivityCalculator.MaxRawDays} days.");

        var legalEntity = access.Value!.LegalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct)
            : null;
        var zone = AttendancePeriodCalculator.ResolveTimezone(legalEntity?.Timezone);
        var zoneName = zone == TimeZoneInfo.Utc ? "UTC" : zone.Id;

        if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
            return Result<EmployeeActivityByHourResponse>.Success(new EmployeeActivityByHourResponse(p.From, p.To, false, zoneName, null, []));

        var from = AttendanceTodayStateService.GetLocalDayWindow(p.From, zone).Start;
        var to = AttendanceTodayStateService.GetLocalDayWindow(p.To, zone).End;
        var slots = await snapshots.GetActiveSecondsByHalfHourAsync(tenantId, request.EmployeeId, from, to, ct);
        var last = await snapshots.GetLastActiveAtAsync(tenantId, request.EmployeeId, from, to, ct);

        return Result<EmployeeActivityByHourResponse>.Success(new EmployeeActivityByHourResponse(
            p.From, p.To, true, zoneName, last, EmployeeWorkActivityCalculator.HourlyActiveMinutes(slots, zone)));
    }
}
```

> If `ONEVO.Application.Features.OrgStructure.LegalEntity.RepositoryInterfaces` fails to resolve `ILegalEntityRepository`, use the same `using` that `TimeAttendance/Services/EmployeeAttendancePeriodReader.cs` uses. Confirm the name of `GetLocalDayWindow`'s return members (`Start`/`End`, as `EmployeeAttendancePeriodReader` uses them).

`GetEmployeeAppUsageQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeAppUsage;

public sealed class GetEmployeeAppUsageQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IAppUsageSnapshotRepository apps,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAppUsageQuery, Result<EmployeeAppUsageResponse>>
{
    public const string ModulePermission = "monitoring:read";

    public async Task<Result<EmployeeAppUsageResponse>> Handle(GetEmployeeAppUsageQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAppUsageResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeAppUsageResponse>.Forbidden("You do not have access to this employee's activity.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAppUsageResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var p = period.Value!;
        if (EmployeeWorkActivityCalculator.ExceedsRawRange(p.From, p.To))
            return Result<EmployeeAppUsageResponse>.Failure($"The period cannot exceed {EmployeeWorkActivityCalculator.MaxRawDays} days.");

        if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ApplicationTracking, ct))
            return Result<EmployeeAppUsageResponse>.Success(new EmployeeAppUsageResponse(p.From, p.To, false, 0, []));

        var (from, to) = EmployeeWorkActivityCalculator.UtcWindow(p.From, p.To);
        var totals = await apps.GetMinutesByProcessAsync(tenantId, request.EmployeeId, from, to, ct);
        var top = EmployeeWorkActivityCalculator.TopProcessNames(totals, EmployeeWorkActivityCalculator.TopApps);
        var samples = top.Count == 0
            ? Array.Empty<AppProcessSampleRow>()
            : await apps.GetSamplesForProcessesAsync(tenantId, request.EmployeeId, from, to, top, ct);

        return Result<EmployeeAppUsageResponse>.Success(new EmployeeAppUsageResponse(
            p.From, p.To, true, totals.Sum(t => t.Samples),
            EmployeeWorkActivityCalculator.AppUsage(totals, samples, EmployeeWorkActivityCalculator.TopApps)));
    }
}
```

`GetEmployeeWorkPatternQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeWorkPattern;

/// <summary>Focus blocks (WorkPatternWindowClassifier per UTC day, summed), top app by minutes and
/// the longest contiguous idle stretch in the period.</summary>
public sealed class GetEmployeeWorkPatternQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IActivitySnapshotRepository snapshots,
    IMeetingSignalRepository meetings,
    IAppUsageSnapshotRepository apps,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeWorkPatternQuery, Result<EmployeeWorkPatternSummaryResponse>>
{
    public const string ModulePermission = "monitoring:read";

    public async Task<Result<EmployeeWorkPatternSummaryResponse>> Handle(GetEmployeeWorkPatternQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkPatternSummaryResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeWorkPatternSummaryResponse>.Forbidden("You do not have access to this employee's activity.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeWorkPatternSummaryResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var p = period.Value!;
        if (EmployeeWorkActivityCalculator.ExceedsRawRange(p.From, p.To))
            return Result<EmployeeWorkPatternSummaryResponse>.Failure($"The period cannot exceed {EmployeeWorkActivityCalculator.MaxRawDays} days.");

        if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
            return Result<EmployeeWorkPatternSummaryResponse>.Success(new EmployeeWorkPatternSummaryResponse(p.From, p.To, false, 0, null, 0, null));

        var (from, to) = EmployeeWorkActivityCalculator.UtcWindow(p.From, p.To);
        var windows = await snapshots.GetWindowsByEmployeeRangeAsync(tenantId, request.EmployeeId, from, to, ct);
        var signals = await meetings.GetByEmployeeRangeAsync(tenantId, request.EmployeeId, from, to, ct);

        // Same per-UTC-day classification the nightly job and my-work-pattern use.
        var signalsByDay = signals.ToLookup(s => DateOnly.FromDateTime(s.CapturedAt.UtcDateTime));
        var focusBlocks = windows
            .GroupBy(w => DateOnly.FromDateTime(w.CapturedAt.UtcDateTime))
            .Sum(day => WorkPatternWindowClassifier.Classify(day.ToList(), signalsByDay[day.Key].ToList()).FocusBlocks);

        string? topApp = null;
        var topAppMinutes = 0;
        if (await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ApplicationTracking, ct))
        {
            var totals = await apps.GetMinutesByProcessAsync(tenantId, request.EmployeeId, from, to, ct);
            var top = totals.OrderByDescending(t => t.Samples).ThenBy(t => t.ProcessName).FirstOrDefault();
            topApp = top?.ProcessName;
            topAppMinutes = top?.Samples ?? 0;
        }

        return Result<EmployeeWorkPatternSummaryResponse>.Success(new EmployeeWorkPatternSummaryResponse(
            p.From, p.To, true, focusBlocks, topApp, topAppMinutes, EmployeeWorkActivityCalculator.LongestIdle(windows)));
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkActivityHandlersTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeWorkActivity tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/GetEmployeeWorkActivityHandlersTests.cs
git commit -m "feat(monitoring): activity-by-hour, app-usage and work-pattern queries

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Controller actions, architecture test, timing check

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Modify: `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs`

- [ ] **Step 1: Add the failing architecture rows** (same Theory as 4A)

```csharp
    [InlineData("work-activity/activity-by-hour", "GetWorkActivityByHour")]
    [InlineData("work-activity/app-usage", "GetWorkActivityAppUsage")]
    [InlineData("work-activity/work-pattern", "GetWorkActivityWorkPattern")]
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~EmployeesControllerArchitectureTests"`
Expected: 3 FAIL.

- [ ] **Step 3: Add the actions** (plus the three `using` lines for the query namespaces)

```csharp
    /// <summary>Work & Activity hourly chart: active minutes per local hour of day over the period (max 31 days).</summary>
    [HttpGet("{id:guid}/work-activity/activity-by-hour")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkActivityByHour(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeActivityByHourQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Application usage card: top 5 apps with minutes, sessions and last use (max 31 days).</summary>
    [HttpGet("{id:guid}/work-activity/app-usage")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkActivityAppUsage(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAppUsageQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Work pattern card: focus blocks, top app and longest idle period (max 31 days).</summary>
    [HttpGet("{id:guid}/work-activity/work-pattern")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkActivityWorkPattern(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkPatternQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/ONEVO.Tests.Architecture` and then `dotnet test tests/ONEVO.Tests.Unit`
Expected: all PASS.

- [ ] **Step 5: Timing check on real PostgreSQL**

Pick an employee with a full month of tray data (or seed about 31 × 480 activity and app-usage rows), then call each endpoint 10 times for the current month:

```bash
curl -s -o /dev/null -w "%{time_total}\n" -b cookies.txt "{apiBase}/api/v1/employees/{id}/work-activity/work-pattern"
```

Expected: every call under 0.4s. If `work-pattern` is slower, run `EXPLAIN ANALYZE` on the `activity_snapshots` range query and confirm it uses the `(tenant_id, employee_id, captured_at)` index. Record the numbers in the PR description.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(corehr): expose work & activity agent endpoints

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `overview/activity` includes today

The Recorded activity totals (Plan 3B) read persisted daily summaries only, so today is missing until the nightly job runs. The hourly chart and app usage above include today, so without this task the cards on one tab would disagree.

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeOverview/GetEmployeeActivityOverview/GetEmployeeActivityOverviewQueryHandler.cs` (created by 3B)
- Modify: `tests/ONEVO.Tests.Unit/Features/Monitoring/GetEmployeeActivityOverviewQueryHandlerTests.cs` (created by 3B)

- [ ] **Step 1: Write the failing tests**

In 3B's `GetEmployeeActivityOverviewQueryHandlerTests`:
- add the field `private readonly Mock<IActivityLiveDaySummary> _live = new();`
- change `CreateHandler()` to `new(_guard.Object, _employees.Object, _summaries.Object, _live.Object, _toggles.Object, _user.Object, _clock.Object)`
- add `using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;` if it is missing

The fixture's today is 2026-09-15, and its `Day(...)` rows are dated `SepFrom`. An un-setup `_live` returns `null`, so 3B's existing tests are unaffected.

```csharp
    [Fact]
    public async Task Handle_AddsTheLiveSummaryForToday_WhenNoPersistedRowExistsYet()
    {
        ArrangeSummaries(SepFrom, SepTo, Day(100, 20, 15));
        _live.Setup(l => l.ComposeAsync(_tenantId, _employeeId, new DateOnly(2026, 9, 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummaryDto { TotalActiveMinutes = 30, TotalIdleMinutes = 5, TotalMeetingMinutes = 10 });

        var result = await CreateHandler().Handle(
            new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        result.Value!.ActiveMinutes.Should().Be(130);
        result.Value.IdleMinutes.Should().Be(25);
        result.Value.MeetingMinutes.Should().Be(25);
        result.Value.DaysWithData.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DoesNotComposeToday_ForAPastPeriod()
    {
        ArrangeSummaries(AugFrom, AugTo, Day(100, 20, 15));

        await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, AugFrom, AugTo), CancellationToken.None);

        _live.VerifyNoOtherCalls();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeActivityOverviewQueryHandlerTests"`
Expected: build FAIL, because the handler has no `IActivityLiveDaySummary` parameter.

- [ ] **Step 3: Implement**

Add `IActivityLiveDaySummary live` to the primary constructor right after `IActivityDailySummaryRepository summaries`. Replace `MeasureAsync` with:

```csharp
    private async Task<EmployeeActivityMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, EmployeePeriod period, CancellationToken ct)
    {
        var rows = (await summaries.GetRangeAsync(tenantId, employeeId, period.From, period.To, ct))
            .Select(r => (r.Date, Active: r.TotalActiveMinutes, Idle: r.TotalIdleMinutes, Meeting: r.TotalMeetingMinutes))
            .ToList();

        // Today has no persisted summary until the nightly job runs; compose it live (same source the
        // attendance day-detail uses) so this card agrees with the hourly chart and app usage.
        var today = clock.Today;
        if (period.From <= today && today <= period.To && rows.All(r => r.Date != today))
        {
            var liveDay = await live.ComposeAsync(tenantId, employeeId, today, ct);
            if (liveDay is not null)
                rows.Add((today, liveDay.TotalActiveMinutes, liveDay.TotalIdleMinutes, liveDay.TotalMeetingMinutes));
        }

        return new EmployeeActivityMetrics(
            rows.Sum(r => r.Active),
            rows.Sum(r => r.Idle),
            rows.Sum(r => r.Meeting),
            rows.Count(r => r.Active + r.Idle + r.Meeting > 0));
    }
```

Also add `using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;` if it is not already there (3B imports it for `IMonitoringToggleResolver`).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeActivityOverviewQueryHandlerTests"`
Expected: PASS, both the new test and 3B's existing ones.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeOverview/GetEmployeeActivityOverview/GetEmployeeActivityOverviewQueryHandler.cs tests/ONEVO.Tests.Unit/Features/Monitoring/GetEmployeeActivityOverviewQueryHandlerTests.cs
git commit -m "feat(monitoring): include today's live activity in the overview activity card

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- Mockup coverage:
  - Recorded activity: 3B totals (Task 6 adds today), plus the hourly chart and last activity (Task 4).
  - Application usage: Task 4.
  - Work pattern: Task 4.
- **Consistency:** focus uses the shared classifier (Task 1). Today is included everywhere (Task 6). UTC days everywhere except the documented hourly exception.
- **Names used across tasks:**
  - `ActivitySlotRow`, `AppProcessMinutesRow`, `AppProcessSampleRow`
  - Repository methods: `GetActiveSecondsByHalfHourAsync`, `GetLastActiveAtAsync`, `GetWindowsByEmployeeRangeAsync`, `GetMinutesByProcessAsync`, `GetSamplesForProcessesAsync`, `GetByEmployeeRangeAsync`
  - `EmployeeWorkActivityCalculator.{MaxRawDays, TopApps, UtcWindow, ExceedsRawRange, HourlyActiveMinutes, AppUsage, TopProcessNames, LongestIdle}`
  - `WorkPatternTotals.FocusBlocks`
- **Known limits:**
  - Timezones with 45-minute offsets land approximately in the hourly chart.
  - `work-pattern` reads a month of projected activity rows. Task 5 Step 5 checks its latency.
