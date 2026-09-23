# Calendar Recurrence Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the already-shipped Calendar Core (`feature/calendar-core`) with real RFC 5545 recurring-event support: RRULE expansion at query time, and this-event/all-events/this-and-following edit and cancel semantics for recurring occurrences.

**Architecture:** `Ical.Net` expands a recurring master's `RecurrenceRule` into virtual occurrence instants at query time (nothing is persisted per-occurrence); a detached-occurrence or cancellation-marker row is a normal `calendar_events` row that points back at its master via `RecurrenceParentId`/`RecurrenceOriginalStart` and suppresses/replaces exactly one virtual occurrence.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), MediatR, `Ical.Net` (RFC 5545 recurrence expansion), xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-08-31-calendar-r1-recurrence-and-frontend-design.md` (this plan implements "Part 1 — Backend: Recurrence Data Model" only; "Part 2 — Frontend Architecture" is a separate plan in the frontend repo, written after this one so its API calls match this plan's actual shipped contract).

## Global Constraints

- Branch off `feature/calendar-core` (this repo's already-shipped, already-tested Calendar Core work), not `development`.
- Recurrence expansion uses `Ical.Net` — do not hand-roll RRULE date math.
- A **master** recurring event: `Recurrence != "none"`, `RecurrenceRule` set, `RecurrenceParentId == null`.
- A **detached occurrence**: `RecurrenceParentId` set, `RecurrenceOriginalStart` set, `IsRecurrenceCancelled == false`, `Recurrence == "none"`.
- A **cancellation marker**: same shape as a detached occurrence but `IsRecurrenceCancelled == true` — never returned by any list/query endpoint.
- A master's own literal DB row is never returned to callers directly — every occurrence of a recurring series, including its first, is represented as a synthesized virtual occurrence with a deterministic id. This is the only way to avoid double-counting the master's first occurrence (once as its own row, once as a synthesized occurrence) — see Task 5.
- `CalendarEventItem`/`CalendarEventViewModel` gain three new **trailing, defaulted** positional-record parameters (`IsRecurringOccurrence = false, RecurrenceMasterId = null, OriginalStart = null`) specifically so every existing call site in `CreateCalendarEventCommandHandler`/`UpdateCalendarEventCommandHandler` and their tests keeps compiling unchanged.
- Every write touching more than one row atomically (`EditThisAndFollowing`) runs inside `IUnitOfWork.ExecuteInTransactionAsync`.

---

### Task 1: Schema — recurrence columns, EF config, migration

**Files:**
- Modify: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventConfiguration.cs`
- Migration: generated via `dotnet ef migrations add AddCalendarRecurrenceColumns`

**Interfaces:**
- Produces: `CalendarEvent.RecurrenceParentId : Guid?`, `.RecurrenceOriginalStart : DateTimeOffset?`, `.IsRecurrenceCancelled : bool`.

- [ ] **Step 1: Add the three properties to `CalendarEvent`**

Add to the existing `CalendarEvent` class (after `ExternalUpdatedAt`):

```csharp
    public Guid? RecurrenceParentId { get; set; }
    public DateTimeOffset? RecurrenceOriginalStart { get; set; }
    public bool IsRecurrenceCancelled { get; set; }
```

- [ ] **Step 2: Add the index and self-referencing FK to `CalendarEventConfiguration`**

Add inside `Configure(...)`, after the existing `HasIndex` calls:

```csharp
        builder.HasIndex(e => new { e.TenantId, e.RecurrenceParentId })
            .HasDatabaseName("ix_calendar_events_tenant_id_recurrence_parent_id");

        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.RecurrenceParentId)
            .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3: Build to confirm it compiles**

Stop the running backend dev server first (locks the DLLs). Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Generate and apply the migration**

```bash
export ConnectionStrings__MigrationConnection="Host=localhost;Port=5432;Database=OnevoDb;Username=onevo_migrator;Password=dapiyshanth@19"
dotnet ef migrations add AddCalendarRecurrenceColumns --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: a new `AddCalendarRecurrenceColumns` migration with three `AddColumn` calls, one `CreateIndex`, one `AddForeignKey` — no RLS SQL block is needed here (no new table; the existing `calendar_events` RLS policy already covers these new columns). `database update` prints `Done.`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventConfiguration.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(calendar): add recurrence-occurrence columns to calendar_events"
```

---

### Task 2: Deterministic occurrence-id helper

**Files:**
- Create: `src/ONEVO.Application/Common/Helpers/DeterministicGuid.cs`
- Test: `tests/ONEVO.Tests.Unit/Common/Helpers/DeterministicGuidTests.cs`

**Interfaces:**
- Produces: `DeterministicGuid.Create(Guid namespaceId, string name) : Guid` — RFC 4122 version-5 (SHA-1 name-based) UUID. Same `(namespaceId, name)` always produces the same GUID.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Common/Helpers/DeterministicGuidTests.cs
using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common.Helpers;

public sealed class DeterministicGuidTests
{
    private static readonly Guid Namespace = Guid.Parse("6f1f9b2a-6c1e-4b7a-9c2e-8f6a1d2b3c4d");

    [Fact]
    public void Create_SameInputs_ReturnsSameGuid()
    {
        var a = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");
        var b = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Create_DifferentName_ReturnsDifferentGuid()
    {
        var a = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");
        var b = DeterministicGuid.Create(Namespace, "master-1|2026-09-08T09:00:00Z");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Create_DifferentNamespace_ReturnsDifferentGuid()
    {
        var otherNamespace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var a = DeterministicGuid.Create(Namespace, "master-1|2026-09-01T09:00:00Z");
        var b = DeterministicGuid.Create(otherNamespace, "master-1|2026-09-01T09:00:00Z");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Create_ReturnsVersion5Variant2Guid()
    {
        var guid = DeterministicGuid.Create(Namespace, "any-name");
        var bytes = guid.ToByteArray();

        Assert.Equal(5, bytes[7] >> 4); // version nibble
        Assert.Equal(0x80, bytes[8] & 0xC0); // RFC 4122 variant
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DeterministicGuidTests" --nologo`
Expected: FAIL - `DeterministicGuid` does not exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// src/ONEVO.Application/Common/Helpers/DeterministicGuid.cs
using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Application.Common.Helpers;

/// <summary>RFC 4122 version-5 (SHA-1 name-based) UUID generation - the same (namespaceId, name)
/// pair always produces the same GUID. Used to give a virtual recurring-event occurrence a stable
/// id derived from (masterEventId, occurrenceStart) without persisting a row for it.</summary>
public static class DeterministicGuid
{
    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        using var sha1 = SHA1.Create();
        sha1.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
        sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
        var hash = sha1.Hash!;

        var newGuid = new byte[16];
        Array.Copy(hash, 0, newGuid, 0, 16);

        newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4)); // version 5
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80); // RFC 4122 variant

        SwapByteOrder(newGuid);
        return new Guid(newGuid);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        SwapBytes(guid, 0, 3);
        SwapBytes(guid, 1, 2);
        SwapBytes(guid, 4, 5);
        SwapBytes(guid, 6, 7);
    }

    private static void SwapBytes(byte[] guid, int left, int right)
        => (guid[left], guid[right]) = (guid[right], guid[left]);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DeterministicGuidTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Common/Helpers/DeterministicGuid.cs tests/ONEVO.Tests.Unit/Common/Helpers/DeterministicGuidTests.cs
git commit -m "feat(calendar): add DeterministicGuid helper for stable virtual-occurrence ids"
```

---

### Task 3: `ICalendarRecurrenceExpander` (Ical.Net wrapper)

**Files:**
- Modify: `src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj` (add `Ical.Net` package reference)
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarRecurrenceExpander.cs`
- Create: `src/ONEVO.Infrastructure/Services/Calendar/IcalNetRecurrenceExpander.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register the service)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/IcalNetRecurrenceExpanderTests.cs`

**Interfaces:**
- Produces: `ICalendarRecurrenceExpander.Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to) : IReadOnlyList<DateTimeOffset>`.

- [ ] **Step 1: Add the Ical.Net package reference**

```bash
dotnet add src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj package Ical.Net
```
(Do not pin an exact version - let NuGet resolve current latest stable.)

- [ ] **Step 2: Write the interface**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarRecurrenceExpander.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public interface ICalendarRecurrenceExpander
{
    /// <summary>Occurrence start instants for a master's RRULE within [from, to]. Capped at 500
    /// occurrences - R1's month-grid queries are always bounded to ~6 weeks so this is never
    /// realistically hit; a longer-running series just returns its first 500 matches in range.</summary>
    IReadOnlyList<DateTimeOffset> Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to);
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/IcalNetRecurrenceExpanderTests.cs
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class IcalNetRecurrenceExpanderTests
{
    private readonly IcalNetRecurrenceExpander _sut = new();

    [Fact]
    public void Expand_Daily_ReturnsOneOccurrencePerDayInRange()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=DAILY", seriesStart, from, to);

        Assert.Equal(4, result.Count); // Sep 1, 2, 3, 4 (to=Sep 5 00:00 is exclusive)
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), result[0]);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), result[3]);
    }

    [Fact]
    public void Expand_Weekly_DefaultsToSeriesStartWeekday()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero); // Tuesday
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=WEEKLY", seriesStart, from, to);

        Assert.Equal(5, result.Count); // Sep 1, 8, 15, 22, 29
        Assert.All(result, d => Assert.Equal(DayOfWeek.Tuesday, d.DayOfWeek));
    }

    [Fact]
    public void Expand_Monthly_DefaultsToSeriesStartDayOfMonth()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=MONTHLY", seriesStart, from, to);

        Assert.Equal(3, result.Count); // Sep 15, Oct 15, Nov 15
        Assert.All(result, d => Assert.Equal(15, d.Day));
    }

    [Fact]
    public void Expand_WithUntil_StopsAtUntilDate()
    {
        var seriesStart = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=DAILY;UNTIL=20260903T090000Z", seriesStart, from, to);

        Assert.Equal(3, result.Count); // Sep 1, 2, 3
    }

    [Fact]
    public void Expand_RangeStartsMidSeries_OnlyReturnsOccurrencesFromRangeStart()
    {
        var seriesStart = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero); // series began in August
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Expand("FREQ=DAILY", seriesStart, from, to);

        Assert.Equal(7, result.Count); // Sep 1-7
        Assert.All(result, d => Assert.True(d >= from));
    }
}
```

If any assertion's exact count is off once run against the real package, read `Ical.Net`'s `GetOccurrences`/`TakeWhileBefore` XML docs (installed under the NuGet package cache, or via IDE IntelliSense) to confirm inclusive/exclusive boundary behavior before changing the expander's logic - do not guess.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~IcalNetRecurrenceExpanderTests" --nologo`
Expected: FAIL - `IcalNetRecurrenceExpander` does not exist yet.

- [ ] **Step 5: Write the implementation**

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/IcalNetRecurrenceExpander.cs
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Calendar;

public class IcalNetRecurrenceExpander : ICalendarRecurrenceExpander
{
    private const int MaxOccurrences = 500;

    public IReadOnlyList<DateTimeOffset> Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to)
    {
        var pattern = new RecurrencePattern(recurrenceRule);
        var calendarEvent = new CalendarEvent
        {
            DtStart = new CalDateTime(seriesStart.UtcDateTime),
            RecurrenceRules = [pattern]
        };

        var rangeStart = new CalDateTime(from.UtcDateTime);
        var rangeEnd = new CalDateTime(to.UtcDateTime);

        return calendarEvent.GetOccurrences(rangeStart)
            .TakeWhileBefore(rangeEnd)
            .Take(MaxOccurrences)
            .Select(o => new DateTimeOffset(o.Period.StartTime.AsUtc, TimeSpan.Zero))
            .ToList();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass, fixing boundary assumptions if needed**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~IcalNetRecurrenceExpanderTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 5` (after any boundary-condition corrections from Step 3's note)

- [ ] **Step 7: Register the service in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, add the using directives near the other Calendar ones added in Calendar Core:

```csharp
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.Services.Calendar;
```

And near the other `AddScoped` calls:

```csharp
        services.AddScoped<ICalendarRecurrenceExpander, IcalNetRecurrenceExpander>();
```

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ src/ONEVO.Infrastructure/Services/Calendar/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/IcalNetRecurrenceExpanderTests.cs
git commit -m "feat(calendar): add ICalendarRecurrenceExpander backed by Ical.Net"
```

---

### Task 4: Repository additions for masters and children

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventRepositoryTests.cs`

**Interfaces:**
- Produces: `GetRecurringMastersForCallerAsync(tenantId, userId, employeeId, to, ct)`, `GetChildrenForMasterAsync(tenantId, masterId, ct)` (tracked - both the query-merge read path and the this-and-following re-parent write path use it), `GetTrackedChildByOriginalStartAsync(tenantId, masterId, originalStart, ct)`.

- [ ] **Step 1: Update the interface**

Add to `ICalendarEventRepository`, and change the doc-comment on the existing `GetInDateRangeForCallerAsync`:

```csharp
    /// <summary>Events in [from, to] where the caller is the creator or a participant. Excludes
    /// recurring masters (Recurrence != "none" AND RecurrenceParentId == null) - those are
    /// expanded into virtual occurrences separately, never returned as their own literal row, to
    /// avoid double-counting a master's first occurrence. Excludes cancellation markers always.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Recurring master rows the caller created or participates in, whose series could
    /// produce an occurrence at or before `to` - the caller expands each one's RecurrenceRule to
    /// find which occurrences actually fall in the query's [from, to] window.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Every detached-occurrence and cancellation-marker row belonging to one master -
    /// tracked, since both the query-merge (read-only) and this-and-following re-parent (write)
    /// call sites use it.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetChildrenForMasterAsync(Guid tenantId, Guid masterId, CancellationToken ct = default);

    /// <summary>The tracked child row (detached or cancellation-marker) for one exact occurrence,
    /// or null if that occurrence has never been edited/cancelled before.</summary>
    Task<CalendarEvent?> GetTrackedChildByOriginalStartAsync(
        Guid tenantId, Guid masterId, DateTimeOffset originalStart, CancellationToken ct = default);
```

(This replaces the existing `GetInDateRangeForCallerAsync` doc-comment in place - the method signature itself is unchanged.)

- [ ] **Step 2: Update the failing/new repository tests**

Add to `EfCalendarEventRepositoryTests.cs` (keep the two existing tests as-is):

```csharp
    [Fact]
    public async Task GetInDateRangeForCallerAsync_ExcludesRecurringMastersAndCancellationMarkers()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var master = MakeEvent(startDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        master.Recurrence = CalendarRecurrences.Weekly;
        master.RecurrenceRule = "FREQ=WEEKLY";
        db.CalendarEvents.Add(master);

        var cancelledChild = MakeEvent(startDate: new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));
        cancelledChild.RecurrenceParentId = master.Id;
        cancelledChild.RecurrenceOriginalStart = cancelledChild.StartDate;
        cancelledChild.IsRecurrenceCancelled = true;
        db.CalendarEvents.Add(cancelledChild);

        var detachedChild = MakeEvent(startDate: new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero));
        detachedChild.RecurrenceParentId = master.Id;
        detachedChild.RecurrenceOriginalStart = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        db.CalendarEvents.Add(detachedChild);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetInDateRangeForCallerAsync(
            TenantId, master.CreatedById, EmployeeId, RangeStart, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(detachedChild.Id, result[0].Id);
    }

    [Fact]
    public async Task GetRecurringMastersForCallerAsync_ReturnsOnlyMastersCreatedByCaller_StartingBeforeTo()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        await using var db = BuildInMemoryDb(currentUser.Object);

        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var mine = MakeEvent(startDate: new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero));
        mine.Recurrence = CalendarRecurrences.Weekly;
        mine.RecurrenceRule = "FREQ=WEEKLY";
        db.CalendarEvents.Add(mine);
        await db.SaveChangesAsync();

        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var someoneElses = MakeEvent(startDate: new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero));
        someoneElses.Recurrence = CalendarRecurrences.Weekly;
        someoneElses.RecurrenceRule = "FREQ=WEEKLY";
        db.CalendarEvents.Add(someoneElses);

        var startsTooLate = MakeEvent(startDate: RangeEnd.AddDays(5));
        startsTooLate.Recurrence = CalendarRecurrences.Weekly;
        startsTooLate.RecurrenceRule = "FREQ=WEEKLY";
        db.CalendarEvents.Add(startsTooLate);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetRecurringMastersForCallerAsync(TenantId, UserId, EmployeeId, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(mine.Id, result[0].Id);
    }

    [Fact]
    public async Task GetChildrenForMasterAsync_ReturnsDetachedAndCancelledChildren()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var masterId = Guid.NewGuid();
        var child1 = MakeEvent(startDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        child1.RecurrenceParentId = masterId;
        var child2 = MakeEvent(startDate: new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));
        child2.RecurrenceParentId = masterId;
        child2.IsRecurrenceCancelled = true;
        var unrelated = MakeEvent(startDate: new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero));
        db.CalendarEvents.AddRange(child1, child2, unrelated);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetChildrenForMasterAsync(TenantId, masterId, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == child1.Id);
        Assert.Contains(result, c => c.Id == child2.Id);
    }

    [Fact]
    public async Task GetTrackedChildByOriginalStartAsync_ReturnsMatchingChild_OrNull()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var masterId = Guid.NewGuid();
        var originalStart = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var child = MakeEvent(startDate: originalStart);
        child.RecurrenceParentId = masterId;
        child.RecurrenceOriginalStart = originalStart;
        db.CalendarEvents.Add(child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);

        var found = await repository.GetTrackedChildByOriginalStartAsync(TenantId, masterId, originalStart, CancellationToken.None);
        var notFound = await repository.GetTrackedChildByOriginalStartAsync(TenantId, masterId, originalStart.AddDays(7), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(child.Id, found!.Id);
        Assert.Null(notFound);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfCalendarEventRepositoryTests" --nologo`
Expected: FAIL - the new repository methods don't exist yet, and the existing exclusion test fails since the current query still returns masters/cancelled rows.

- [ ] **Step 4: Update the implementation**

Replace `GetInDateRangeForCallerAsync`'s body and add the three new methods in `EfCalendarEventRepository`:

```csharp
    public async Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && !e.IsRecurrenceCancelled
                        && (e.RecurrenceParentId != null || e.Recurrence == CalendarRecurrences.None)
                        && e.StartDate <= to && e.EndDate >= from
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .OrderBy(e => e.StartDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.Recurrence != CalendarRecurrences.None
                        && e.RecurrenceParentId == null
                        && e.StartDate <= to
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetChildrenForMasterAsync(Guid tenantId, Guid masterId, CancellationToken ct = default)
        => await _db.CalendarEvents
            .Where(e => e.TenantId == tenantId && e.RecurrenceParentId == masterId)
            .ToListAsync(ct);

    public async Task<CalendarEvent?> GetTrackedChildByOriginalStartAsync(
        Guid tenantId, Guid masterId, DateTimeOffset originalStart, CancellationToken ct = default)
        => await _db.CalendarEvents.FirstOrDefaultAsync(
            e => e.TenantId == tenantId && e.RecurrenceParentId == masterId && e.RecurrenceOriginalStart == originalStart, ct);
```

Add `using ONEVO.Domain.Features.Calendar.Entities;` if not already present (needed for `CalendarRecurrences.None`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfCalendarEventRepositoryTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventRepositoryTests.cs
git commit -m "feat(calendar): add repository support for recurring masters and their children"
```

---

### Task 5: Expand and merge virtual occurrences into `GetCalendarEventsQuery`

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarRecurrenceExpander.Expand` (Task 3), `ICalendarEventRepository.GetRecurringMastersForCallerAsync`/`GetChildrenForMasterAsync` (Task 4), `DeterministicGuid.Create` (Task 2).
- Produces: `CalendarEventItem` gains 3 trailing defaulted params (`IsRecurringOccurrence = false, RecurrenceMasterId = null, OriginalStart = null`).

- [ ] **Step 1: Widen `CalendarEventItem` with trailing defaulted parameters**

```csharp
// src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs
namespace ONEVO.Application.Features.Calendar.DTOs.Responses;

public sealed record CalendarEventItem(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string SourceType,
    string? Color,
    string Recurrence,
    bool IsAllDay,
    string? Timezone,
    string? EventStatus,
    bool IsPrivate,
    string? Location,
    string? MeetingLink,
    string? ExternalSource,
    Guid CreatedById,
    bool IsRecurringOccurrence = false,
    Guid? RecurrenceMasterId = null,
    DateTimeOffset? OriginalStart = null);

public sealed record CalendarEventsResponse(IReadOnlyList<CalendarEventItem> Events);
```

- [ ] **Step 2: Widen the failing tests**

Add to `GetCalendarEventsQueryHandlerTests.cs` (keep the two existing tests as-is - they still pass unchanged since the new constructor params default):

```csharp
    [Fact]
    public async Task Handle_ExpandsRecurringMaster_SkippingDetachedAndCancelledOccurrences()
    {
        var sut = BuildSut();
        var master = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Standup",
            StartDate = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 9, 1, 9, 30, 0, TimeSpan.Zero),
            SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.Daily,
            RecurrenceRule = "FREQ=DAILY", CreatedById = UserId, CreatedAt = DateTimeOffset.UtcNow
        };
        _events.Setup(x => x.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _events.Setup(x => x.GetRecurringMastersForCallerAsync(TenantId, UserId, EmployeeId, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([master]);

        var detachedStart = new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);
        var cancelledStart = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, master.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = master.Id, RecurrenceOriginalStart = detachedStart, IsRecurrenceCancelled = false },
                new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = master.Id, RecurrenceOriginalStart = cancelledStart, IsRecurrenceCancelled = true }
            ]);

        var occurrenceStarts = new[]
        {
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
            detachedStart,
            cancelledStart
        };
        _expander.Setup(x => x.Expand("FREQ=DAILY", master.StartDate, From, To)).Returns(occurrenceStarts);

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Events.Count); // Sep 1 and Sep 2 only - Sep 3 detached, Sep 4 cancelled
        Assert.All(result.Value.Events, e => Assert.True(e.IsRecurringOccurrence));
        Assert.All(result.Value.Events, e => Assert.Equal(master.Id, e.RecurrenceMasterId));
    }
```

Update `BuildSut()` to also construct a `Mock<ICalendarRecurrenceExpander> _expander = new();` field and pass `_expander.Object` into the handler constructor.

- [ ] **Step 3: Run tests to verify the new test fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetCalendarEventsQueryHandlerTests" --nologo`
Expected: FAIL - handler constructor doesn't take an expander yet, and doesn't expand masters.

- [ ] **Step 4: Update the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed class GetCalendarEventsQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander)
    : IRequestHandler<GetCalendarEventsQuery, Result<CalendarEventsResponse>>
{
    private static readonly Guid OccurrenceIdNamespace = Guid.Parse("6f1f9b2a-6c1e-4b7a-9c2e-8f6a1d2b3c4d");

    public async Task<Result<CalendarEventsResponse>> Handle(GetCalendarEventsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);

        var realRows = await events.GetInDateRangeForCallerAsync(
            tenantId, currentUser.UserId, employee?.Id, request.From, request.To, ct);

        var items = realRows.Select(e => new CalendarEventItem(
            e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color,
            e.Recurrence, e.IsAllDay, e.Timezone, e.EventStatus, e.IsPrivate, e.Location,
            e.MeetingLink, e.ExternalSource, e.CreatedById)).ToList();

        var masters = await events.GetRecurringMastersForCallerAsync(tenantId, currentUser.UserId, employee?.Id, request.To, ct);

        foreach (var master in masters)
        {
            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
            var occurrenceStarts = expander.Expand(master.RecurrenceRule!, master.StartDate, request.From, request.To);
            var duration = master.EndDate - master.StartDate;

            foreach (var occurrenceStart in occurrenceStarts)
            {
                var overridden = children.Any(c => c.RecurrenceOriginalStart == occurrenceStart);
                if (overridden)
                    continue; // a detached row is already in realRows; a cancellation is never shown

                var occurrenceId = DeterministicGuid.Create(OccurrenceIdNamespace, $"{master.Id:N}|{occurrenceStart:O}");
                items.Add(new CalendarEventItem(
                    occurrenceId, master.Title, master.Description, occurrenceStart, occurrenceStart + duration,
                    master.SourceType, master.Color, master.Recurrence, master.IsAllDay, master.Timezone,
                    master.EventStatus, master.IsPrivate, master.Location, master.MeetingLink,
                    master.ExternalSource, master.CreatedById,
                    IsRecurringOccurrence: true, RecurrenceMasterId: master.Id, OriginalStart: occurrenceStart));
            }
        }

        return Result<CalendarEventsResponse>.Success(new CalendarEventsResponse(items.OrderBy(i => i.StartDate).ToList()));
    }
}
```

- [ ] **Step 5: Widen the API contracts to match**

In `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`, widen `CalendarEventViewModel` and its mapper the same way:

```csharp
public sealed record CalendarEventViewModel(
    Guid Id, string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    string SourceType, string? Color, string Recurrence, bool IsAllDay, string? Timezone,
    string? EventStatus, bool IsPrivate, string? Location, string? MeetingLink,
    string? ExternalSource, Guid CreatedById,
    bool IsRecurringOccurrence = false, Guid? RecurrenceMasterId = null, DateTimeOffset? OriginalStart = null);
```

And update `CalendarEventViewModelMapper.ToViewModel(this CalendarEventItem dto)` to pass the three new fields through:

```csharp
    public static CalendarEventViewModel ToViewModel(this CalendarEventItem dto) => new(
        dto.Id, dto.Title, dto.Description, dto.StartDate, dto.EndDate, dto.SourceType, dto.Color,
        dto.Recurrence, dto.IsAllDay, dto.Timezone, dto.EventStatus, dto.IsPrivate, dto.Location,
        dto.MeetingLink, dto.ExternalSource, dto.CreatedById,
        dto.IsRecurringOccurrence, dto.RecurrenceMasterId, dto.OriginalStart);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetCalendarEventsQueryHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs
git commit -m "feat(calendar): expand recurring masters into virtual occurrences in GetCalendarEventsQuery"
```

---

### Task 6: `EditRecurringOccurrenceCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/RecurrenceEditScope.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EditRecurringOccurrenceCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.GetTrackedByIdForTenantAsync`, `.GetTrackedChildByOriginalStartAsync`, `.GetChildrenForMasterAsync`, `.AddAsync`, `.Update` (Task 4).
- Produces: `EditRecurringOccurrenceCommand`, `RecurrenceEditScope { ThisEventOnly, AllEvents, ThisAndFollowing }`.

- [ ] **Step 1: Write the scope enum and command**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/RecurrenceEditScope.cs
namespace ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;

public enum RecurrenceEditScope { ThisEventOnly, AllEvents, ThisAndFollowing }
```

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;

public sealed record EditRecurringOccurrenceCommand(
    Guid MasterId,
    DateTimeOffset OriginalStart,
    RecurrenceEditScope Scope,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    string? MeetingLink,
    string? Color) : IRequest<Result<CalendarEventItem>>;
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/EditRecurringOccurrenceCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EditRecurringOccurrenceCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly DateTimeOffset SeriesStart = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OriginalStart = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private EditRecurringOccurrenceCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new EditRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeMaster() => new()
    {
        Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        StartDate = SeriesStart, EndDate = SeriesStart.AddMinutes(30),
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY",
        SourceType = CalendarEventSourceTypes.Manual
    };

    private EditRecurringOccurrenceCommand MakeCommand(RecurrenceEditScope scope) => new(
        MasterId, OriginalStart, scope, "New Title", "New desc",
        OriginalStart.AddHours(1), OriginalStart.AddHours(1).AddMinutes(30), false, "UTC", "Room 2", null, "#ff0000");

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.CreatedById = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotARecurringMaster_ReturnsFailure()
    {
        var sut = BuildSut();
        var notAMaster = MakeMaster();
        notAMaster.Recurrence = CalendarRecurrences.None;
        notAMaster.RecurrenceRule = null;
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(notAMaster);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllEvents_UpdatesMasterDirectly()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", master.Title);
        _events.Verify(x => x.Update(master), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NoExistingChild_CreatesDetachedRow()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == MasterId && e.RecurrenceOriginalStart == OriginalStart && e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_ExistingChild_UpdatesIt()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var existingChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart,
            Title = "Old", StartDate = OriginalStart, EndDate = OriginalStart.AddMinutes(30)
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingChild);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", existingChild.Title);
        _events.Verify(x => x.Update(existingChild), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_SplitsSeriesAndReparentsLaterChildren()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var earlierChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId,
            RecurrenceOriginalStart = OriginalStart.AddDays(-7), Title = "Earlier override"
        };
        var laterChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId,
            RecurrenceOriginalStart = OriginalStart.AddDays(7), Title = "Later override"
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlierChild, laterChild]);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, laterChild.Id, It.IsAny<CancellationToken>())).ReturnsAsync(laterChild);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisAndFollowing), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("UNTIL=", master.RecurrenceRule);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == null && e.Recurrence == CalendarRecurrences.Weekly && e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(MasterId, earlierChild.RecurrenceParentId); // untouched - before the split point
        _events.Verify(x => x.Update(laterChild), Times.Once); // re-parented - on/after the split point
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EditRecurringOccurrenceCommandHandlerTests" --nologo`
Expected: FAIL - `EditRecurringOccurrenceCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;

public sealed class EditRecurringOccurrenceCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<EditRecurringOccurrenceCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        var tenantId = currentUser.TenantId;
        var master = await events.GetTrackedByIdForTenantAsync(tenantId, request.MasterId, ct);
        if (master is null)
            return Result<CalendarEventItem>.NotFound("Calendar event not found.");

        if (master.RecurrenceParentId != null || master.Recurrence == CalendarRecurrences.None)
            return Result<CalendarEventItem>.Failure("This event is not a recurring series.", 400);

        if (master.CreatedById != currentUser.UserId)
            return Result<CalendarEventItem>.Forbidden("Only the series creator can edit this event.");

        return request.Scope switch
        {
            RecurrenceEditScope.AllEvents => await EditAllEventsAsync(master, request, ct),
            RecurrenceEditScope.ThisEventOnly => await EditThisEventOnlyAsync(tenantId, master, request, ct),
            RecurrenceEditScope.ThisAndFollowing => await EditThisAndFollowingAsync(tenantId, master, request, ct),
            _ => Result<CalendarEventItem>.Failure("Unknown edit scope.", 400)
        };
    }

    private async Task<Result<CalendarEventItem>> EditAllEventsAsync(
        CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        ApplyFields(master, request);
        events.Update(master);
        await unitOfWork.SaveChangesAsync(ct);
        return Result<CalendarEventItem>.Success(ToItem(master, isOccurrence: false, masterId: null, originalStart: null));
    }

    private async Task<Result<CalendarEventItem>> EditThisEventOnlyAsync(
        Guid tenantId, CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, ct);
        if (child is null)
        {
            child = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                RecurrenceOriginalStart = request.OriginalStart, Recurrence = CalendarRecurrences.None,
                SourceType = master.SourceType
            };
            ApplyFields(child, request);
            await events.AddAsync(child, ct);
        }
        else
        {
            child.IsRecurrenceCancelled = false;
            ApplyFields(child, request);
            events.Update(child);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<CalendarEventItem>.Success(ToItem(child, isOccurrence: true, masterId: master.Id, originalStart: request.OriginalStart));
    }

    private async Task<Result<CalendarEventItem>> EditThisAndFollowingAsync(
        Guid tenantId, CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var originalRule = master.RecurrenceRule!;
            master.RecurrenceRule = WithUntil(originalRule, request.OriginalStart.AddSeconds(-1));
            events.Update(master);

            var newMaster = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = null,
                Recurrence = master.Recurrence, RecurrenceRule = originalRule, SourceType = master.SourceType
            };
            ApplyFields(newMaster, request);
            await events.AddAsync(newMaster, innerCt);

            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, innerCt);
            foreach (var child in children.Where(c => c.RecurrenceOriginalStart >= request.OriginalStart))
            {
                var tracked = await events.GetTrackedByIdForTenantAsync(tenantId, child.Id, innerCt);
                if (tracked is null) continue;
                tracked.RecurrenceParentId = newMaster.Id;
                events.Update(tracked);
            }

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(newMaster, isOccurrence: true, masterId: newMaster.Id, originalStart: request.StartDate));
        }, ct);
    }

    private static void ApplyFields(CalendarEvent target, EditRecurringOccurrenceCommand request)
    {
        target.Title = request.Title.Trim();
        target.Description = request.Description;
        target.StartDate = request.StartDate;
        target.EndDate = request.EndDate;
        target.IsAllDay = request.IsAllDay;
        target.Timezone = request.Timezone;
        target.Location = request.Location;
        target.MeetingLink = request.MeetingLink;
        target.Color = request.Color;
    }

    private static CalendarEventItem ToItem(CalendarEvent e, bool isOccurrence, Guid? masterId, DateTimeOffset? originalStart) => new(
        e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color, e.Recurrence, e.IsAllDay,
        e.Timezone, e.EventStatus, e.IsPrivate, e.Location, e.MeetingLink, e.ExternalSource, e.CreatedById,
        isOccurrence, masterId, originalStart);

    private static string WithUntil(string recurrenceRule, DateTimeOffset until)
    {
        var parts = recurrenceRule.Split(';').Where(p => !p.StartsWith("UNTIL=", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add($"UNTIL={until.UtcDateTime:yyyyMMddTHHmmssZ}");
        return string.Join(';', parts);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EditRecurringOccurrenceCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/ tests/ONEVO.Tests.Unit/Features/Calendar/EditRecurringOccurrenceCommandHandlerTests.cs
git commit -m "feat(calendar): add EditRecurringOccurrenceCommand (this-event/all-events/this-and-following)"
```

---

### Task 7: `CancelRecurringOccurrenceCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/CancelRecurringOccurrenceCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/CancelRecurringOccurrenceCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CancelRecurringOccurrenceCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.GetTrackedByIdForTenantAsync`, `.GetTrackedChildByOriginalStartAsync`, `.AddAsync`, `.Update` (Task 4).
- Produces: `CancelRecurringOccurrenceCommand(Guid MasterId, DateTimeOffset OriginalStart) : IRequest<Result>`.

- [ ] **Step 1: Write the command and its failing tests**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/CancelRecurringOccurrenceCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;

public sealed record CancelRecurringOccurrenceCommand(Guid MasterId, DateTimeOffset OriginalStart) : IRequest<Result>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CancelRecurringOccurrenceCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CancelRecurringOccurrenceCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly DateTimeOffset OriginalStart = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CancelRecurringOccurrenceCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new CancelRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeMaster() => new()
    {
        Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY", SourceType = CalendarEventSourceTypes.Manual
    };

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.CreatedById = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoExistingChild_CreatesCancellationMarker()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == MasterId && e.RecurrenceOriginalStart == OriginalStart && e.IsRecurrenceCancelled),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingChild_MarksItCancelled_Idempotently()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var existingChild = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingChild);

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existingChild.IsRecurrenceCancelled);
        _events.Verify(x => x.Update(existingChild), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CancelRecurringOccurrenceCommandHandlerTests" --nologo`
Expected: FAIL - `CancelRecurringOccurrenceCommandHandler` does not exist yet.

- [ ] **Step 3: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/CancelRecurringOccurrenceCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;

public sealed class CancelRecurringOccurrenceCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CancelRecurringOccurrenceCommand, Result>
{
    public async Task<Result> Handle(CancelRecurringOccurrenceCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var master = await events.GetTrackedByIdForTenantAsync(tenantId, request.MasterId, ct);
        if (master is null)
            return Result.NotFound("Calendar event not found.");

        if (master.RecurrenceParentId != null || master.Recurrence == CalendarRecurrences.None)
            return Result.Failure("This event is not a recurring series.", 400);

        if (master.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the series creator can cancel an occurrence.");

        var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, ct);
        if (child is null)
        {
            child = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                RecurrenceOriginalStart = request.OriginalStart, IsRecurrenceCancelled = true,
                Title = master.Title, StartDate = request.OriginalStart,
                EndDate = request.OriginalStart + (master.EndDate - master.StartDate),
                SourceType = master.SourceType, Recurrence = CalendarRecurrences.None
            };
            await events.AddAsync(child, ct);
        }
        else
        {
            child.IsRecurrenceCancelled = true;
            events.Update(child);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CancelRecurringOccurrenceCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/ tests/ONEVO.Tests.Unit/Features/Calendar/CancelRecurringOccurrenceCommandHandlerTests.cs
git commit -m "feat(calendar): add CancelRecurringOccurrenceCommand"
```

---

### Task 8: Controller endpoints, contracts, and full verification

**Files:**
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`

**Interfaces:**
- Consumes: `EditRecurringOccurrenceCommand` (Task 6), `CancelRecurringOccurrenceCommand` (Task 7).
- Produces: `PUT api/v1/calendar/{id}/occurrence`, `DELETE api/v1/calendar/{id}/occurrence?originalStart=`.

- [ ] **Step 1: Add the occurrence-edit request contract**

Add to `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`:

```csharp
public sealed record EditRecurringOccurrenceRequest(
    DateTimeOffset OriginalStart, string Scope, string Title, string? Description,
    DateTimeOffset StartDate, DateTimeOffset EndDate, bool IsAllDay, string? Timezone,
    string? Location, string? MeetingLink, string? Color);
```

- [ ] **Step 2: Add the two controller actions**

Add to `CalendarController`, alongside `using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;` and `using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;`:

```csharp
    [HttpPut("{id:guid}/occurrence")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> EditOccurrence(Guid id, [FromBody] EditRecurringOccurrenceRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<RecurrenceEditScope>(request.Scope, ignoreCase: true, out var scope))
            return Problem("Invalid scope. Expected 'ThisEventOnly' or 'AllEvents'.", statusCode: 400);

        var result = await _mediator.Send(new EditRecurringOccurrenceCommand(
            id, request.OriginalStart, scope, request.Title, request.Description, request.StartDate,
            request.EndDate, request.IsAllDay, request.Timezone, request.Location, request.MeetingLink, request.Color), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}/occurrence")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> CancelOccurrence(Guid id, [FromQuery] DateTimeOffset originalStart, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelRecurringOccurrenceCommand(id, originalStart), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 3: Build everything**

Stop the running backend dev server first. Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: all Calendar tests pass (the pre-existing ~3334 plus the ~24 new/updated ones across Tasks 2-7), zero new failures elsewhere.

- [ ] **Step 5: Run the architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass - the two new controller actions live on the existing `CalendarController`, so no architecture rule needs updating.

- [ ] **Step 6: Manually verify end-to-end**

Restart the backend dev server. Authenticated as the seeded `dapi` tenant owner:
1. `POST /api/v1/calendar` with `recurrence: "weekly"`, a `recurrenceRule` your test client builds as `FREQ=WEEKLY` (no `UNTIL`), starting on a known weekday.
2. `GET /api/v1/calendar?from=&to=` across a 4-week window - confirm 4 occurrences come back, all `isRecurringOccurrence: true`, sharing the same `recurrenceMasterId`, with distinct `id`s.
3. `PUT /api/v1/calendar/{masterId}/occurrence` with `scope: "ThisEventOnly"` and a new title for the 2nd occurrence's `originalStart` - re-run the GET and confirm only that occurrence's title changed.
4. `DELETE /api/v1/calendar/{masterId}/occurrence?originalStart=` for the 3rd occurrence - re-run the GET and confirm it's gone, the others remain.
5. `PUT /api/v1/calendar/{masterId}` (plain endpoint, i.e. "all events") with a new title - re-run the GET and confirm the 1st and 4th occurrences (never individually touched) picked up the new title, while the edited 2nd occurrence's override title is unchanged.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs
git commit -m "feat(calendar): wire EditRecurringOccurrenceCommand/CancelRecurringOccurrenceCommand into CalendarController"
```
