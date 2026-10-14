# Meetings as Tasks — Phase 0B (Attendance sync classification) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Teams or Zoom meeting's attendance is retried until the provider has a final report, every page of it is stored, and when no data ever arrives the meeting says so ("no data"), never "nobody attended".

**Architecture:** A pure policy turns each fetch outcome (`Failed`, `NotReady` or `Final`) plus the time since the event ended into the meeting's next `attendance_sync_status`, or "retry later". Both clients follow pagination. The Teams client also reports whether an attendance report exists at all. Both sync jobs share one recorder that applies the policy. A one-time data step re-queues meetings that were marked synced with zero rows.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + Moq, Testcontainers PostgreSQL; Angular 21 + Vitest for the one UI message.

**Spec:** `docs/superpowers/specs/2026-10-13-meeting-task-phase0-1-design.md` §3.2 row P0-2 and §3.2.1. Companion plans: `2026-10-14-meeting-task-phase0a-calendar-notifications.md` and `2026-10-14-meeting-task-phase0c-meeting-provisioning.md`. 0B depends on neither.

## Global Constraints

- Worktrees: backend `C:\Users\User\onexso-worktrees\be-meeting-task`, frontend `C:\Users\User\onexso-worktrees\fe-meeting-task`, both on branch `feature/meeting-task-phase0-1`.
- Classification (spec §3.2.1, verbatim intent):
  - Exception or non-2xx from either provider → no state change, retried next run. Still failing 24 h after `EndDate` → `sync_failed`.
  - Teams, no attendance report yet → retried until 24 h after `EndDate`, then `attendance_unavailable`.
  - Teams, a report exists (any number of records, including 0) → store all pages → `synced`.
  - Zoom, 200 with ≥1 participant → store all pages → `synced`.
  - Zoom, 200 with 0 participants → retry until 24 h after `EndDate`, then `attendance_unavailable`.
- New nullable column `calendar_event_meetings.attendance_sync_status` (`synced`, `attendance_unavailable` or `sync_failed`). `LastAttendanceSyncedAt` is set **only** with `synced`.
- **`attendance_unavailable` means "no data received", never "nobody attended".**
- Due filter: event `EndDate < now − 30 min` and `attendance_sync_status is null` (and still `Active`).
- A missing connection, or a connection that needs re-authorisation, counts as a fetch **failure** (it is an API failure from the user's point of view), so it ends in `sync_failed` after 24 h instead of retrying forever.
- **Historical repair ruling:** the spec asks to first verify provider retention, then either re-fetch or mark unavailable. This plan re-queues every zero-row "synced" meeting (`last_attendance_synced_at = null`, status null) and lets the classifier decide. A report still available is stored (`synced`). A report gone is a missing report or a 0-participant reply after the 24 h cut-off, which becomes `attendance_unavailable`. An API failure becomes `sync_failed`. Either way the result is correct without knowing retention. The cost is one extra fetch per affected meeting. Meetings that already have attendance rows are marked `synced`.
- Pagination: Graph follows `@odata.nextLink`, Zoom follows `next_page_token` with `page_size=300`. Both stop with an exception after 100 pages (a runaway loop is a failure, retried, then `sync_failed`).
- Migrations: `ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none" dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`. Data steps start with `SET LOCAL app.tenant_context_mode = 'admin';`.
- Commit and push to the branch only, **no PR**. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A Teams report that exists but has zero records after reading every page** must be `synced` with 0 rows, not retried forever. Tested in Task 2 (client) and Task 3 (job).
2. **Records spread over several Graph pages** must all be stored. Tested in Task 2.
3. **A fetch that keeps throwing** stays retryable for 24 h and then becomes `sync_failed`, never `attendance_unavailable`. Tested in Task 3.
4. **A meeting that ended 10 minutes ago** is not fetched yet (30-minute grace). Tested in Task 3.
5. **The attendance panel for an `attendance_unavailable` meeting** must not read as "nobody joined". Tested in Task 4.

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs` (modify) | `AttendanceSyncStatus` + status constants |
| `src/ONEVO.Application/Features/Calendar/Services/AttendanceSyncPolicy.cs` (create) | Pure classification |
| `src/ONEVO.Infrastructure/Persistence/DataMigrations/AttendanceSyncStatusBackfill.cs` (create) | Frozen repair SQL |
| `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs`, `MicrosoftGraphMeetingClient.cs`, `ZoomMeetingClient.cs` (modify) | Pagination; Teams "report exists" |
| `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs`, `EfCalendarEventMeetingRepository.cs` (modify) | `DueAttendanceSync`, grace, status filter |
| `src/ONEVO.Infrastructure/Services/Calendar/AttendanceSyncRecorder.cs` (create) | Applies the policy and writes rows + status |
| `src/ONEVO.Infrastructure/Services/Calendar/{Teams,Zoom}AttendanceSyncJob.cs` (modify) | Classify each fetch |
| `GetEventMeetingAttendance*` + `CalendarContracts.cs` (modify) | Expose `SyncStatus` |
| frontend `calendar-event.model.ts`, `calendar-event-attendance.component.ts` (modify) | Honest status message |

---

### Task 1: Status column, pure policy, and the historical repair

**Files:**
- Modify: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventMeetingConfiguration.cs`. Find it with `grep -rl "CalendarEventMeeting>" src/ONEVO.Infrastructure/Persistence/Configurations`.
- Create: `src/ONEVO.Application/Features/Calendar/Services/AttendanceSyncPolicy.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/DataMigrations/AttendanceSyncStatusBackfill.cs`
- Create (generated, then edited): `src/ONEVO.Infrastructure/Migrations/<ts>_AddMeetingAttendanceSyncStatus.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Calendar/AttendanceSyncPolicyTests.cs`
- Create: `tests/ONEVO.Tests.Integration/Calendar/AttendanceSyncStatusBackfillIntegrationTests.cs`

**Interfaces:**
- Produces: `CalendarEventMeeting.AttendanceSyncStatus` (`string?`). `CalendarAttendanceSyncStatuses.Synced|Unavailable|Failed` = `"synced"`, `"attendance_unavailable"`, `"sync_failed"`. `enum AttendanceFetchOutcome { Failed, NotReady, Final }`. `AttendanceSyncPolicy.Grace` (30 min), `AttendanceSyncPolicy.GiveUpAfter` (24 h). `static string? AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome outcome, DateTimeOffset eventEnd, DateTimeOffset now)`: null means "change nothing, retry next run".

- [ ] **Step 1: Write the failing policy tests**

`tests/ONEVO.Tests.Unit/Features/Calendar/AttendanceSyncPolicyTests.cs`:

```csharp
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class AttendanceSyncPolicyTests
{
    private static readonly DateTimeOffset EventEnd = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(48)]
    public void Final_IsSynced_AtAnyAge(int hoursAfterEnd)
        => Assert.Equal(CalendarAttendanceSyncStatuses.Synced,
            AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.Final, EventEnd, EventEnd.AddHours(hoursAfterEnd)));

    [Fact]
    public void NotReady_BeforeTheCutOff_RetriesLater()
        => Assert.Null(AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.NotReady, EventEnd, EventEnd.AddHours(23).AddMinutes(59)));

    [Fact]
    public void NotReady_AtTheCutOff_IsUnavailable()
        => Assert.Equal(CalendarAttendanceSyncStatuses.Unavailable,
            AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.NotReady, EventEnd, EventEnd.AddHours(24)));

    [Fact]
    public void Failed_BeforeTheCutOff_RetriesLater()
        => Assert.Null(AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.Failed, EventEnd, EventEnd.AddHours(2)));

    [Fact]
    public void Failed_AfterTheCutOff_IsSyncFailed_NotUnavailable()
        => Assert.Equal(CalendarAttendanceSyncStatuses.Failed,
            AttendanceSyncPolicy.NextStatus(AttendanceFetchOutcome.Failed, EventEnd, EventEnd.AddHours(30)));
}
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `AttendanceSyncPolicy` / `CalendarAttendanceSyncStatuses` do not exist.

- [ ] **Step 2: Implement the column and the policy**

In `CalendarEventMeeting.cs`, after `CalendarEventMeetingStatuses` add:

```csharp
/// <summary>Outcome of attendance sync (spec §3.2.1). Null = not decided yet (still retried).
/// <see cref="Unavailable"/> means "no data received", never "nobody attended".</summary>
public static class CalendarAttendanceSyncStatuses
{
    public const string Synced = "synced";
    public const string Unavailable = "attendance_unavailable";
    public const string Failed = "sync_failed";
}
```

and in the class, after `LastAttendanceSyncedAt`:

```csharp
    public string? AttendanceSyncStatus { get; set; }
```

In the configuration's `Configure`, add `builder.Property(m => m.AttendanceSyncStatus).HasMaxLength(32);`.

`src/ONEVO.Application/Features/Calendar/Services/AttendanceSyncPolicy.cs`:

```csharp
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>What one attendance fetch told us.</summary>
public enum AttendanceFetchOutcome
{
    /// <summary>Exception, non-2xx, missing connection or re-auth needed.</summary>
    Failed,
    /// <summary>Teams: no report yet. Zoom: 200 with 0 participants (can't tell "not ready" from "nobody joined").</summary>
    NotReady,
    /// <summary>Teams: a report exists (even with 0 records). Zoom: 200 with at least one participant.</summary>
    Final
}

/// <summary>Spec §3.2.1 classification, pure so both sync jobs (and their tests) share it.</summary>
public static class AttendanceSyncPolicy
{
    /// <summary>Reports appear some time after a meeting ends; don't ask before this.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

    /// <summary>After this long past the event's end, stop retrying and record why.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    /// <summary>The meeting's next attendance_sync_status, or null to change nothing and retry next run.</summary>
    public static string? NextStatus(AttendanceFetchOutcome outcome, DateTimeOffset eventEnd, DateTimeOffset now)
    {
        var givingUp = now >= eventEnd + GiveUpAfter;
        return outcome switch
        {
            AttendanceFetchOutcome.Final => CalendarAttendanceSyncStatuses.Synced,
            AttendanceFetchOutcome.NotReady => givingUp ? CalendarAttendanceSyncStatuses.Unavailable : null,
            _ => givingUp ? CalendarAttendanceSyncStatuses.Failed : null
        };
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~AttendanceSyncPolicyTests"`
Expected: 6 passed.

- [ ] **Step 3: Write the frozen repair SQL**

`src/ONEVO.Infrastructure/Persistence/DataMigrations/AttendanceSyncStatusBackfill.cs`:

```csharp
namespace ONEVO.Infrastructure.Persistence.DataMigrations;

/// <summary>
/// Data step of migration AddMeetingAttendanceSyncStatus. FROZEN: the migration and its
/// integration test run this exact text. Never edit it; add a new migration instead.
///
/// Before this column, the jobs stamped last_attendance_synced_at even when the provider returned
/// nothing, so zero-row "synced" meetings were never retried. Meetings with rows are marked synced;
/// zero-row ones are re-queued, and the classifier then stores a late report, or (past the 24 h
/// cut-off) records attendance_unavailable / sync_failed.
/// </summary>
public static class AttendanceSyncStatusBackfill
{
    public const string Sql = """
        SET LOCAL app.tenant_context_mode = 'admin';

        UPDATE calendar_event_meetings m
        SET attendance_sync_status = 'synced'
        WHERE m.last_attendance_synced_at IS NOT NULL
          AND m.attendance_sync_status IS NULL
          AND EXISTS (SELECT 1 FROM calendar_event_meeting_attendances a WHERE a.calendar_event_meeting_id = m.id);

        UPDATE calendar_event_meetings m
        SET last_attendance_synced_at = NULL
        WHERE m.last_attendance_synced_at IS NOT NULL
          AND m.attendance_sync_status IS NULL;
        """;
}
```

Check the attendance table and column names with `grep -rn "ToTable\|CalendarEventMeetingId" src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/*Attendance*`. If either differs, fix it in the SQL **before** the first commit, because the SQL is frozen once committed.

- [ ] **Step 4: Generate the migration and insert the data step**

```bash
ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none" dotnet ef migrations add AddMeetingAttendanceSyncStatus --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```

In the generated `Up`, add `using ONEVO.Infrastructure.Persistence.DataMigrations;` and place `migrationBuilder.Sql(AttendanceSyncStatusBackfill.Sql);` **after** the `AddColumn<string>("attendance_sync_status", ...)`.

- [ ] **Step 5: Integration test for the repair**

`tests/ONEVO.Tests.Integration/Calendar/AttendanceSyncStatusBackfillIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.DataMigrations;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.Calendar;

public sealed class AttendanceSyncStatusBackfillIntegrationTests
{
    private static async Task<CalendarEventMeeting> AddMeetingAsync(ApplicationDbContext db, Guid tenantId, DateTimeOffset? syncedAt, int attendanceRows)
    {
        var evt = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Title = "Review", StartDate = DateTimeOffset.UtcNow.AddDays(-3),
            EndDate = DateTimeOffset.UtcNow.AddDays(-3).AddHours(1), SourceType = CalendarEventSourceTypes.Manual
        };
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = evt.Id, ExternalCalendarConnectionId = Guid.NewGuid(),
            Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = $"m-{Guid.NewGuid():N}", JoinUrl = "https://teams.example/j",
            Status = CalendarEventMeetingStatuses.Active, LastAttendanceSyncedAt = syncedAt, CreatedAt = DateTimeOffset.UtcNow
        };
        db.PersonalCalendarEvents.Add(evt);
        db.CalendarEventMeetings.Add(meeting);
        for (var i = 0; i < attendanceRows; i++)
            db.CalendarEventMeetingAttendances.Add(new CalendarEventMeetingAttendance
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = meeting.Id,
                ExternalParticipantName = $"P{i}", JoinedAt = evt.StartDate, CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();
        return meeting;
    }

    [Fact]
    public async Task SyncedWithRows_IsMarkedSynced_ZeroRowsIsRequeued_NeverSyncedIsUntouched()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var withRows = await AddMeetingAsync(db, h.TenantId, DateTimeOffset.UtcNow.AddDays(-2), attendanceRows: 2);
        var zeroRows = await AddMeetingAsync(db, h.TenantId, DateTimeOffset.UtcNow.AddDays(-2), attendanceRows: 0);
        var neverSynced = await AddMeetingAsync(db, h.TenantId, syncedAt: null, attendanceRows: 0);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync(AttendanceSyncStatusBackfill.Sql);
            await tx.CommitAsync();
        }

        var rows = await db.CalendarEventMeetings.AsNoTracking().Where(m => m.TenantId == h.TenantId).ToDictionaryAsync(m => m.Id);
        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, rows[withRows.Id].AttendanceSyncStatus);
        Assert.NotNull(rows[withRows.Id].LastAttendanceSyncedAt);
        Assert.Null(rows[zeroRows.Id].AttendanceSyncStatus);
        Assert.Null(rows[zeroRows.Id].LastAttendanceSyncedAt);
        Assert.Null(rows[neverSynced.Id].AttendanceSyncStatus);
        Assert.Null(rows[neverSynced.Id].LastAttendanceSyncedAt);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~AttendanceSyncStatusBackfillIntegrationTests"`
Expected: 1 passed. A compile error on `CalendarEventMeetingAttendance` fields means the entity's property names differ; read the entity and adjust the test only.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs src/ONEVO.Infrastructure/Persistence/Configurations src/ONEVO.Application/Features/Calendar/Services/AttendanceSyncPolicy.cs src/ONEVO.Infrastructure/Persistence/DataMigrations/AttendanceSyncStatusBackfill.cs src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Unit/Features/Calendar/AttendanceSyncPolicyTests.cs tests/ONEVO.Tests.Integration/Calendar/AttendanceSyncStatusBackfillIntegrationTests.cs
git commit -m "feat(calendar-attendance): attendance_sync_status, the classification policy and the zero-row repair

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Clients read every page; Teams says whether a report exists

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs:50-93`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/ZoomMeetingClient.cs:90-116`
- Modify: `src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs:113` (compile only; Task 3 rewrites it)
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs`, `ZoomMeetingClientTests.cs`, `TeamsAttendanceSyncJobTests.cs` (compile only)

**Interfaces:**
- Produces: `public sealed record TeamsAttendanceResult(bool ReportExists, IReadOnlyList<TeamsAttendanceRecordDto> Records);` and `Task<TeamsAttendanceResult> ITeamsMeetingClient.GetAttendanceAsync(string accessToken, string externalMeetingId, CancellationToken ct)`. Zoom keeps `Task<IReadOnlyList<ZoomAttendanceRecordDto>>`, now covering every page.

- [ ] **Step 1: Write the failing client tests**

In `MicrosoftGraphMeetingClientTests`, replace `GetAttendanceAsync_NoReports_ReturnsEmpty` with:

```csharp
    [Fact]
    public async Task GetAttendanceAsync_NoReports_SaysNoReportExists()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1/attendanceReports", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = Array.Empty<object>() }) };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.False(result.ReportExists);
        Assert.Empty(result.Records);
    }
```

In `GetAttendanceAsync_HasReport_ReturnsFlattenedIntervalsFromMostRecentReport`, change `var record = Assert.Single(result);` to `Assert.True(result.ReportExists);` followed by `var record = Assert.Single(result.Records);`.

Append:

```csharp
    [Fact]
    public async Task GetAttendanceAsync_ReportWithZeroRecords_IsAReportThatExists()
    {
        var handler = new StubHandler(request => request.RequestUri!.ToString().EndsWith("attendanceReports")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new[] { new { id = "report-1" } } }) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = Array.Empty<object>() }) });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.True(result.ReportExists);
        Assert.Empty(result.Records);
    }

    [Fact]
    public async Task GetAttendanceAsync_RecordsOnSeveralPages_FollowsNextLinkAndReturnsThemAll()
    {
        const string page2 = "https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1/attendanceReports/report-1/attendanceRecords?$skiptoken=abc";
        object Record(string name) => new
        {
            identity = new { displayName = name, upn = $"{name}@acme.com" },
            attendanceIntervals = new[] { new { joinDateTime = "2026-09-23T10:00:00Z", leaveDateTime = "2026-09-23T10:30:00Z" } }
        };
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("attendanceReports"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new[] { new { id = "report-1" } } }) };
            if (url == page2)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new[] { Record("grace") } }) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"value":[{"identity":{"displayName":"ada","upn":"ada@acme.com"},"attendanceIntervals":[{"joinDateTime":"2026-09-23T10:00:00Z","leaveDateTime":"2026-09-23T10:30:00Z"}]}],"@odata.nextLink":"{{page2}}"}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.Equal(new[] { "ada", "grace" }, result.Records.Select(r => r.ParticipantName));
    }
```

In `ZoomMeetingClientTests.GetAttendanceAsync_ReturnsFlattenedParticipants`, change the expected URL to `"https://api.zoom.us/v2/past_meetings/987654321/participants?page_size=300"` and append:

```csharp
    [Fact]
    public async Task GetAttendanceAsync_SeveralPages_FollowsNextPageTokenAndReturnsThemAll()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            object Participant(string name) => new { name, user_email = $"{name}@acme.com", join_time = "2026-09-23T10:00:00Z", leave_time = "2026-09-23T10:30:00Z" };
            return url.EndsWith("next_page_token=tok2")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { participants = new[] { Participant("grace") }, next_page_token = "" }) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { participants = new[] { Participant("ada") }, next_page_token = "tok2" }) };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", CancellationToken.None);

        Assert.Equal(new[] { "ada", "grace" }, result.Select(r => r.ParticipantName));
    }
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `ReportExists` / `Records` do not exist.

- [ ] **Step 2: Implement**

In `ITeamsMeetingClient.cs`, after `TeamsAttendanceRecordDto` add:

```csharp
/// <summary><see cref="ReportExists"/> = false means Teams has not produced an attendance report
/// yet (retry); true with no records means the report is final and nobody joined.</summary>
public sealed record TeamsAttendanceResult(bool ReportExists, IReadOnlyList<TeamsAttendanceRecordDto> Records);
```

and change `GetAttendanceAsync`'s return type to `Task<TeamsAttendanceResult>`.

Replace `MicrosoftGraphMeetingClient.GetAttendanceAsync` with:

```csharp
    private const int MaxPages = 100;

    public async Task<TeamsAttendanceResult> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // One report per session; the last one is the most recent. Every list is paged by Graph.
        var reports = await GetAllPagesAsync(
            accessToken, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports",
            "list attendance reports", ct);
        if (reports.Count == 0)
            return new TeamsAttendanceResult(false, []);
        var reportId = reports[^1].GetProperty("id").GetString();

        var records = await GetAllPagesAsync(
            accessToken,
            $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports/{reportId}/attendanceRecords",
            "list attendance records", ct);

        var result = new List<TeamsAttendanceRecordDto>();
        foreach (var record in records)
        {
            var identity = record.TryGetProperty("identity", out var i) ? i : default;
            var name = identity.ValueKind != JsonValueKind.Undefined && identity.TryGetProperty("displayName", out var n) ? n.GetString() : null;
            var email = identity.ValueKind != JsonValueKind.Undefined && identity.TryGetProperty("upn", out var e) ? e.GetString() : null;

            foreach (var interval in record.GetProperty("attendanceIntervals").EnumerateArray())
            {
                var joined = interval.GetProperty("joinDateTime").GetDateTimeOffset();
                var left = interval.TryGetProperty("leaveDateTime", out var l) && l.ValueKind != JsonValueKind.Null
                    ? l.GetDateTimeOffset() : (DateTimeOffset?)null;
                result.Add(new TeamsAttendanceRecordDto(name, email, joined, left));
            }
        }
        return new TeamsAttendanceResult(true, result);
    }

    private async Task<List<JsonElement>> GetAllPagesAsync(string accessToken, string firstUrl, string operation, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? url = firstUrl;
        for (var page = 1; url is not null; page++)
        {
            if (page > MaxPages)
                throw new InvalidOperationException($"Microsoft Graph {operation} exceeded {MaxPages} pages.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, ct);
            await EnsureSuccessOrLogAsync(response, operation, ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            items.AddRange(doc.RootElement.GetProperty("value").EnumerateArray().Select(element => element.Clone()));
            url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        return items;
    }
```

Replace `ZoomMeetingClient.GetAttendanceAsync` with:

```csharp
    private const int MaxAttendancePages = 100;

    public async Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // Zoom's past-meeting participants report, paged by next_page_token (empty on the last page).
        var result = new List<ZoomAttendanceRecordDto>();
        string? pageToken = null;
        for (var page = 1; ; page++)
        {
            if (page > MaxAttendancePages)
                throw new InvalidOperationException($"Zoom participants report exceeded {MaxAttendancePages} pages.");

            var url = $"https://api.zoom.us/v2/past_meetings/{externalMeetingId}/participants?page_size=300"
                + (string.IsNullOrEmpty(pageToken) ? "" : $"&next_page_token={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, ct);
            await EnsureSuccessOrLogAsync(response, "list past meeting participants", ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var participant in doc.RootElement.GetProperty("participants").EnumerateArray())
            {
                var name = participant.TryGetProperty("name", out var n) ? n.GetString() : null;
                var email = participant.TryGetProperty("user_email", out var e) ? e.GetString() : null;
                var joined = participant.GetProperty("join_time").GetDateTimeOffset();
                var left = participant.TryGetProperty("leave_time", out var l) && l.ValueKind != JsonValueKind.Null
                    ? l.GetDateTimeOffset() : (DateTimeOffset?)null;
                result.Add(new ZoomAttendanceRecordDto(name, email, joined, left));
            }

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var token) ? token.GetString() : null;
            if (string.IsNullOrEmpty(pageToken))
                return result;
        }
    }
```

In `TeamsAttendanceSyncJob.SyncOneMeetingAsync`, change `var records = await teamsClient.GetAttendanceAsync(...)` to `var records = (await teamsClient.GetAttendanceAsync(accessToken, meeting.ExternalMeetingId, ct)).Records;`. This is a compile-only change; Task 3 replaces the method.

In `TeamsAttendanceSyncJobTests`, wrap each `.ReturnsAsync(new List<TeamsAttendanceRecordDto> { ... })` on `GetAttendanceAsync` as `.ReturnsAsync(new TeamsAttendanceResult(true, new List<TeamsAttendanceRecordDto> { ... }))`. Same records, compile-only.

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~MeetingClientTests|FullyQualifiedName~AttendanceSyncJobTests"`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs tests/ONEVO.Tests.Unit/Features/Calendar
git commit -m "fix(calendar-attendance): read every page of Teams and Zoom attendance; Teams reports whether a report exists

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Due filter with grace, shared recorder, and classifying sync jobs

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs`
- Create: `src/ONEVO.Infrastructure/Services/Calendar/AttendanceSyncRecorder.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs`, `ZoomAttendanceSyncJob.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommandHandler.cs:95`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingRepositoryTests.cs`
- Rewrite: `tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs`, `ZoomAttendanceSyncJobTests.cs`

**Interfaces:**
- Consumes: `AttendanceSyncPolicy`, `CalendarAttendanceSyncStatuses` (Task 1), `TeamsAttendanceResult` (Task 2).
- Produces: `public sealed record DueAttendanceSync(CalendarEventMeeting Meeting, DateTimeOffset EventEndDate);`. `Task<IReadOnlyList<DueAttendanceSync>> ICalendarEventMeetingRepository.GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing repository tests**

In `EfCalendarEventMeetingRepositoryTests`, change `Assert.Equal(due.Id, result[0].Id);` to `Assert.Equal(due.Id, result[0].Meeting.Id);`, and `Assert.Equal(zoomMeeting.Id, meeting.Id);` to `Assert.Equal(zoomMeeting.Id, meeting.Meeting.Id);`. Then append:

```csharp
    [Fact]
    public async Task GetDueForAttendanceSyncAsync_EndedInsideTheGrace_IsNotDueYet_AndCarriesTheEventEnd()
    {
        await using var db = BuildInMemoryDb();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var justEnded = MakeCalendarEvent(now.AddHours(-1), now.AddMinutes(-10));
        var endedLongAgo = MakeCalendarEvent(now.AddHours(-2), now.AddMinutes(-31));
        db.PersonalCalendarEvents.AddRange(justEnded, endedLongAgo);
        db.CalendarEventMeetings.AddRange(MakeMeeting(justEnded.Id), MakeMeeting(endedLongAgo.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(d => d.UtcNow).Returns(now);

        var result = await new EfCalendarEventMeetingRepository(db, clock.Object)
            .GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.MicrosoftTeams, CancellationToken.None);

        var due = Assert.Single(result);
        Assert.Equal(endedLongAgo.EndDate, due.EventEndDate);
    }

    [Fact]
    public async Task GetDueForAttendanceSyncAsync_ExcludesMeetingsWithADecidedStatus()
    {
        await using var db = BuildInMemoryDb();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var evt = MakeCalendarEvent(now.AddHours(-3), now.AddHours(-2));
        var decided = MakeMeeting(evt.Id);
        decided.AttendanceSyncStatus = CalendarAttendanceSyncStatuses.Unavailable;
        db.PersonalCalendarEvents.Add(evt);
        db.CalendarEventMeetings.Add(decided);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(d => d.UtcNow).Returns(now);

        var result = await new EfCalendarEventMeetingRepository(db, clock.Object)
            .GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.MicrosoftTeams, CancellationToken.None);

        Assert.Empty(result);
    }
```

- [ ] **Step 2: Rewrite the job tests (failing)**

Replace `tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class TeamsAttendanceSyncJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public Mock<ICalendarEventMeetingRepository> Meetings { get; } = new();
        public Mock<IExternalCalendarConnectionRepository> Connections { get; } = new();
        public Mock<ICalendarConnectionTokenProvider> Tokens { get; } = new();
        public Mock<ITeamsMeetingClient> Teams { get; } = new();
        public Mock<ICalendarEventMeetingAttendanceRepository> Attendances { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<CalendarEventMeeting> Due { get; } = [];

        public CalendarEventMeeting AddDue(TimeSpan endedAgo, string externalId = "graph-meeting-1")
        {
            var meeting = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = Guid.NewGuid(), ExternalCalendarConnectionId = Guid.NewGuid(),
                Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = externalId,
                JoinUrl = "https://teams.microsoft.com/l/meetup-join/abc", Status = CalendarEventMeetingStatuses.Active
            };
            Due.Add(meeting);
            _ends[meeting.Id] = Now - endedAgo;
            var connection = new ExternalCalendarConnection
            {
                Id = meeting.ExternalCalendarConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(),
                Provider = CalendarExternalSources.OutlookCalendar, ExternalAccountEmail = "me@acme.com",
                RefreshTokenEncrypted = [1], Status = ExternalCalendarConnectionStatuses.Active
            };
            Connections.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
            Tokens.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");
            return meeting;
        }

        private readonly Dictionary<Guid, DateTimeOffset> _ends = [];

        public async Task RunAsync()
        {
            Meetings.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.MicrosoftTeams, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Due.Select(m => new DueAttendanceSync(m, _ends[m.Id])).ToList());
            var tenants = new Mock<ITenantRepository>();
            tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Tenant> { new() { Id = TenantId, Name = "Acme", Slug = "acme", Status = TenantStatus.Active } });
            tenants.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Tenant>());
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(c => c.UtcNow).Returns(Now);

            var services = new ServiceCollection();
            services.AddSingleton(tenants.Object);
            services.AddSingleton(Meetings.Object);
            services.AddSingleton(Connections.Object);
            services.AddSingleton(Tokens.Object);
            services.AddSingleton(Teams.Object);
            services.AddSingleton(Attendances.Object);
            services.AddSingleton(UnitOfWork.Object);
            services.AddSingleton(clock.Object);
            services.AddSingleton(Mock.Of<IWritableTenantContext>());
            services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
            await new TeamsAttendanceSyncJob(services.BuildServiceProvider(), NullLogger<TeamsAttendanceSyncJob>.Instance)
                .RunOnceAsync(CancellationToken.None);
        }
    }

    private static TeamsAttendanceRecordDto Record(string name) => new(name, $"{name}@acme.com", Now.AddHours(-2), Now.AddHours(-1));

    [Fact]
    public async Task ReportWithRecords_StoresThem_AndMarksSynced()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(1));
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsAttendanceResult(true, [Record("ada"), Record("grace")]));

        await rig.RunAsync();

        rig.Attendances.Verify(a => a.AddRangeAsync(
            It.Is<IEnumerable<CalendarEventMeetingAttendance>>(r => r.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, meeting.AttendanceSyncStatus);
        Assert.Equal(Now, meeting.LastAttendanceSyncedAt);
    }

    [Fact]
    public async Task ReportWithZeroRecords_IsSyncedWithNoRows()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(1));
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsAttendanceResult(true, []));

        await rig.RunAsync();

        rig.Attendances.Verify(a => a.AddRangeAsync(It.IsAny<IEnumerable<CalendarEventMeetingAttendance>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task NoReportYet_WithinTheCutOff_ChangesNothing()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(2));
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsAttendanceResult(false, []));

        await rig.RunAsync();

        Assert.Null(meeting.AttendanceSyncStatus);
        Assert.Null(meeting.LastAttendanceSyncedAt);
        rig.Meetings.Verify(m => m.Update(It.IsAny<CalendarEventMeeting>()), Times.Never);
    }

    [Fact]
    public async Task NoReport_PastTheCutOff_IsUnavailable_AndNeverStampsSyncedAt()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(25));
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsAttendanceResult(false, []));

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Unavailable, meeting.AttendanceSyncStatus);
        Assert.Null(meeting.LastAttendanceSyncedAt);
    }

    [Fact]
    public async Task FetchThrows_WithinTheCutOff_ChangesNothing_PastIt_IsSyncFailed()
    {
        var rig = new Rig();
        var recent = rig.AddDue(TimeSpan.FromHours(3), "recent");
        var old = rig.AddDue(TimeSpan.FromHours(30), "old");
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("500"));

        await rig.RunAsync();

        Assert.Null(recent.AttendanceSyncStatus);
        Assert.Equal(CalendarAttendanceSyncStatuses.Failed, old.AttendanceSyncStatus);
    }

    [Fact]
    public async Task MissingConnection_PastTheCutOff_IsSyncFailed()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(30));
        rig.Connections.Setup(c => c.GetByIdForTenantAsync(TenantId, meeting.ExternalCalendarConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Failed, meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task OneMeetingThrows_TheOtherStillSyncs()
    {
        var rig = new Rig();
        rig.AddDue(TimeSpan.FromHours(1), "broken");
        var ok = rig.AddDue(TimeSpan.FromHours(1), "ok");
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", "broken", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("500"));
        rig.Teams.Setup(t => t.GetAttendanceAsync("access-token", "ok", It.IsAny<CancellationToken>())).ReturnsAsync(new TeamsAttendanceResult(true, [Record("ada")]));

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, ok.AttendanceSyncStatus);
    }
}
```

Replace `tests/ONEVO.Tests.Unit/Features/Calendar/ZoomAttendanceSyncJobTests.cs` with the same file structure, changing:
- the class name to `ZoomAttendanceSyncJobTests`;
- `ITeamsMeetingClient` to `IZoomMeetingClient` (property name `Zoom`);
- provider `CalendarEventMeetingProviders.Zoom`;
- connection provider `CalendarExternalSources.Zoom` and token provider `"zoom"`;
- the job to `ZoomAttendanceSyncJob`;
- `Record` returning `ZoomAttendanceRecordDto`.

The Zoom-specific test cases are these:

```csharp
    [Fact]
    public async Task AtLeastOneParticipant_IsSynced()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(1));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record("ada")]);

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task ZeroParticipants_WithinTheCutOff_ChangesNothing()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(2));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await rig.RunAsync();

        Assert.Null(meeting.AttendanceSyncStatus);
    }

    [Fact]
    public async Task ZeroParticipants_PastTheCutOff_IsUnavailable_NotNobodyJoined()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(25));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Unavailable, meeting.AttendanceSyncStatus);
        rig.Attendances.Verify(a => a.AddRangeAsync(It.IsAny<IEnumerable<CalendarEventMeetingAttendance>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchThrows_PastTheCutOff_IsSyncFailed()
    {
        var rig = new Rig();
        var meeting = rig.AddDue(TimeSpan.FromHours(30));
        rig.Zoom.Setup(z => z.GetAttendanceAsync("access-token", "zoom-meeting-1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("500"));

        await rig.RunAsync();

        Assert.Equal(CalendarAttendanceSyncStatuses.Failed, meeting.AttendanceSyncStatus);
    }
```

In the Zoom rig, `AddDue`'s default `externalId` is `"zoom-meeting-1"`. Check the Zoom connection provider constant (`grep -n "Zoom" src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs`); it is `CalendarExternalSources.Zoom`.

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `DueAttendanceSync` does not exist.

- [ ] **Step 3: Implement the repository change**

In `ICalendarEventMeetingRepository.cs`, add at namespace level:

```csharp
/// <summary>A meeting due for an attendance fetch, with its event's end (the 24 h cut-off counts from it).</summary>
public sealed record DueAttendanceSync(CalendarEventMeeting Meeting, DateTimeOffset EventEndDate);
```

and change the method to `Task<IReadOnlyList<DueAttendanceSync>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default);`.

In `EfCalendarEventMeetingRepository`, replace the method with:

```csharp
    public async Task<IReadOnlyList<DueAttendanceSync>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        // Reports appear some time after a meeting ends: wait out the grace before asking.
        var endedBefore = _dateTime.UtcNow - AttendanceSyncPolicy.Grace;
        var rows = await (
            from meeting in _db.CalendarEventMeetings.AsNoTracking()
            join calendarEvent in _db.PersonalCalendarEvents.AsNoTracking() on meeting.CalendarEventId equals calendarEvent.Id
            where meeting.TenantId == tenantId
                && meeting.Provider == provider
                && meeting.Status == CalendarEventMeetingStatuses.Active
                && meeting.AttendanceSyncStatus == null
                && meeting.LastAttendanceSyncedAt == null
                && calendarEvent.EndDate < endedBefore
            select new { meeting, calendarEvent.EndDate }
        ).ToListAsync(ct);
        return rows.Select(r => new DueAttendanceSync(r.meeting, r.EndDate)).ToList();
    }
```

Add `using ONEVO.Application.Features.Calendar.Services;`.

- [ ] **Step 4: Implement the recorder and the jobs**

`src/ONEVO.Infrastructure/Services/Calendar/AttendanceSyncRecorder.cs`:

```csharp
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>One attendance row as both providers report it.</summary>
internal sealed record FetchedAttendance(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

/// <summary>Applies <see cref="AttendanceSyncPolicy"/> to one fetch and persists the result. Shared by
/// the Teams and Zoom jobs so both classify identically.</summary>
internal static class AttendanceSyncRecorder
{
    public static async Task RecordAsync(
        Guid tenantId, DueAttendanceSync due, AttendanceFetchOutcome outcome, IReadOnlyList<FetchedAttendance> records,
        DateTimeOffset now, ICalendarEventMeetingAttendanceRepository attendances, ICalendarEventMeetingRepository meetings,
        IUnitOfWork unitOfWork, ILogger logger, CancellationToken ct)
    {
        var status = AttendanceSyncPolicy.NextStatus(outcome, due.EventEndDate, now);
        if (status is null)
            return; // not decided yet: retried next run

        var meeting = due.Meeting;
        if (status == CalendarAttendanceSyncStatuses.Synced && records.Count > 0)
        {
            await attendances.AddRangeAsync(records.Select(r => new CalendarEventMeetingAttendance
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = meeting.Id,
                ExternalParticipantName = r.Name, ExternalParticipantEmail = r.Email,
                JoinedAt = r.JoinedAt, LeftAt = r.LeftAt,
                DurationSeconds = r.LeftAt.HasValue ? (int)(r.LeftAt.Value - r.JoinedAt).TotalSeconds : null,
                CreatedAt = now
            }), ct);
        }

        meeting.AttendanceSyncStatus = status;
        meeting.LastAttendanceSyncedAt = status == CalendarAttendanceSyncStatuses.Synced ? now : null;
        meeting.UpdatedAt = now;
        meetings.Update(meeting);
        await unitOfWork.SaveChangesAsync(ct);

        if (status != CalendarAttendanceSyncStatuses.Synced)
            logger.LogWarning(
                "Attendance for meeting {MeetingId} (tenant {TenantId}) recorded as {Status} after {Hours} h.",
                meeting.Id, tenantId, status, AttendanceSyncPolicy.GiveUpAfter.TotalHours);
    }
}
```

In `TeamsAttendanceSyncJob`:
- add `using ONEVO.Application.Features.Calendar.Services;`;
- in `RunOnceAsync`, resolve `var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();` next to the other services;
- replace the per-meeting loop with:

```csharp
                    foreach (var due in await meetings.GetDueForAttendanceSyncAsync(tenant.Id, CalendarEventMeetingProviders.MicrosoftTeams, ct))
                    {
                        try
                        {
                            await SyncOneMeetingAsync(tenant.Id, due, connections, tokenProvider, teamsClient, attendances, meetings, unitOfWork, clock, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogWarning(ex, "Attendance sync failed for meeting {MeetingId} (tenant {TenantId}); skipping.", due.Meeting.Id, tenant.Id);
                        }
                    }
```

- replace `SyncOneMeetingAsync` with:

```csharp
    private async Task SyncOneMeetingAsync(
        Guid tenantId, DueAttendanceSync due,
        IExternalCalendarConnectionRepository connections, ICalendarConnectionTokenProvider tokenProvider,
        ITeamsMeetingClient teamsClient, ICalendarEventMeetingAttendanceRepository attendances,
        ICalendarEventMeetingRepository meetings, IUnitOfWork unitOfWork, IDateTimeProvider clock, CancellationToken ct)
    {
        var outcome = AttendanceFetchOutcome.Failed;
        IReadOnlyList<FetchedAttendance> records = [];
        try
        {
            var connection = await connections.GetByIdForTenantAsync(tenantId, due.Meeting.ExternalCalendarConnectionId, ct);
            var accessToken = connection is null ? null : await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
            if (accessToken is not null)
            {
                var result = await teamsClient.GetAttendanceAsync(accessToken, due.Meeting.ExternalMeetingId, ct);
                outcome = result.ReportExists ? AttendanceFetchOutcome.Final : AttendanceFetchOutcome.NotReady;
                records = result.Records.Select(r => new FetchedAttendance(r.ParticipantName, r.ParticipantEmail, r.JoinedAt, r.LeftAt)).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Teams attendance fetch failed for meeting {MeetingId}; will retry.", due.Meeting.Id);
        }

        await AttendanceSyncRecorder.RecordAsync(
            tenantId, due, outcome, records, clock.UtcNow, attendances, meetings, unitOfWork, logger, ct);
    }
```

`ZoomAttendanceSyncJob` gets the same three changes, with `zoomClient`, provider `CalendarEventMeetingProviders.Zoom`, token provider `"zoom"`, and this classification line in place of the Teams one:

```csharp
                var participants = await zoomClient.GetAttendanceAsync(accessToken, due.Meeting.ExternalMeetingId, ct);
                // 0 participants can't be told apart from "report not ready" (spec §3.2.1).
                outcome = participants.Count > 0 ? AttendanceFetchOutcome.Final : AttendanceFetchOutcome.NotReady;
                records = participants.Select(r => new FetchedAttendance(r.ParticipantName, r.ParticipantEmail, r.JoinedAt, r.LeftAt)).ToList();
```

The jobs' `SyncOneMeetingAsync` changes from `static` to an instance method, because it now uses the `logger` primary-constructor parameter.

In `CreateEventMeetingCommandHandler.cs:95`, after `existingMeeting.LastAttendanceSyncedAt = null;` add `existingMeeting.AttendanceSyncStatus = null;` (a re-created meeting starts undecided).

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~AttendanceSyncJobTests|FullyQualifiedName~EfCalendarEventMeetingRepositoryTests|FullyQualifiedName~CreateEventMeetingCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar src/ONEVO.Infrastructure/Persistence/Repositories/Calendar src/ONEVO.Infrastructure/Services/Calendar tests/ONEVO.Tests.Unit/Features/Calendar
git commit -m "fix(calendar-attendance): retry until a final report, give up after 24 h with an honest status

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The attendance panel says what actually happened

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance/GetEventMeetingAttendanceQuery.cs`, `GetEventMeetingAttendanceQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs:101-106`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/GetEventMeetingAttendanceQueryHandlerTests.cs`
- Frontend: `src/app/modules/calendar/models/calendar-event.model.ts:148-151`, `src/app/modules/calendar/ui/calendar-event-attendance/calendar-event-attendance.component.ts` and its `.spec.ts`

**Interfaces:**
- Produces: `GetEventMeetingAttendanceResult(bool Available, IReadOnlyList<MeetingAttendeeItem> Attendees, string SyncStatus)`. `SyncStatus` is `"none"` (no meeting), `"pending"`, `"synced"`, `"attendance_unavailable"` or `"sync_failed"`. JSON field: `syncStatus`.

- [ ] **Step 1: Write the failing backend tests**

In `GetEventMeetingAttendanceQueryHandlerTests`:
- in `Handle_MeetingSynced_ReturnsAttendees`, change the meeting to `new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = DateTimeOffset.UtcNow, AttendanceSyncStatus = CalendarAttendanceSyncStatuses.Synced }` and add `result.Value.SyncStatus.Should().Be("synced");`;
- in `Handle_NoMeeting_ReturnsEmptyUnavailableResult`, add `result.Value.SyncStatus.Should().Be("none");`;
- in `Handle_MeetingNeverSynced_ReturnsUnavailable`, add `result.Value!.SyncStatus.Should().Be("pending");`.

Append:

```csharp
    [Theory]
    [InlineData(CalendarAttendanceSyncStatuses.Unavailable)]
    [InlineData(CalendarAttendanceSyncStatuses.Failed)]
    public async Task Handle_Undecidable_IsNotAvailable_AndSaysWhy(string status)
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), AttendanceSyncStatus = status });

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeFalse();
        result.Value.SyncStatus.Should().Be(status);
    }
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `SyncStatus` does not exist.

- [ ] **Step 2: Implement the backend**

`GetEventMeetingAttendanceQuery.cs:8` becomes:

```csharp
/// <summary><see cref="SyncStatus"/>: "none" (no online meeting), "pending" (not fetched yet), or the
/// meeting's attendance_sync_status. "attendance_unavailable" means no data was received - not that
/// nobody attended.</summary>
public sealed record GetEventMeetingAttendanceResult(bool Available, IReadOnlyList<MeetingAttendeeItem> Attendees, string SyncStatus);
```

In the handler, replace the body after `var meeting = …;` with:

```csharp
        if (meeting is null)
            return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(false, [], "none"));
        if (meeting.AttendanceSyncStatus != CalendarAttendanceSyncStatuses.Synced)
            return Result<GetEventMeetingAttendanceResult>.Success(
                new GetEventMeetingAttendanceResult(false, [], meeting.AttendanceSyncStatus ?? "pending"));

        var records = await attendances.GetByMeetingIdAsync(tenantId, meeting.Id, ct);
        var items = records.Select(a => new MeetingAttendeeItem(
            a.ExternalParticipantName, a.ExternalParticipantEmail, a.JoinedAt, a.LeftAt, a.DurationSeconds)).ToList();
        return Result<GetEventMeetingAttendanceResult>.Success(
            new GetEventMeetingAttendanceResult(true, items, CalendarAttendanceSyncStatuses.Synced));
```

(add `using ONEVO.Domain.Features.Calendar.Entities;`). In `CalendarContracts.cs`, `MeetingAttendanceViewModel` becomes `(bool Available, IReadOnlyList<MeetingAttendeeViewModel> Attendees, string SyncStatus)`, and its mapper passes `dto.SyncStatus` last.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~GetEventMeetingAttendanceQueryHandlerTests"` → Expected: all pass.

- [ ] **Step 3: Write the failing frontend tests**

In `calendar-event-attendance.component.spec.ts`, change `setup`'s parameter type to `{ available: boolean; attendees: unknown[]; syncStatus?: string }`. In the existing "unavailable" test, pass `syncStatus: 'pending'`. Append:

```ts
  it('explains that attendance arrives after the meeting while it is pending', () => {
    setup({ available: false, attendees: [], syncStatus: 'pending' });
    expect(fixture.nativeElement.textContent).toContain('Attendance appears after the meeting ends');
  });

  it('never says nobody attended when no data was received', () => {
    setup({ available: false, attendees: [], syncStatus: 'attendance_unavailable' });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain("didn't send attendance data");
    expect(text).not.toContain('Nobody joined');
  });

  it('says the fetch failed when it did', () => {
    setup({ available: false, attendees: [], syncStatus: 'sync_failed' });
    expect(fixture.nativeElement.textContent).toContain("couldn't fetch attendance");
  });

  it('says nobody joined only for a final, empty report', () => {
    setup({ available: true, attendees: [], syncStatus: 'synced' });
    expect(fixture.nativeElement.textContent).toContain('Nobody joined this meeting');
  });
```

Run: `cd "C:/Users/User/onexso-worktrees/fe-meeting-task" && npx ng test --watch=false --include src/app/modules/calendar/ui/calendar-event-attendance/calendar-event-attendance.component.spec.ts`
Expected: the 4 new tests FAIL.

- [ ] **Step 4: Implement the frontend**

In `calendar-event.model.ts`, `MeetingAttendanceResponse` becomes:

```ts
export type MeetingAttendanceSyncStatus = 'none' | 'pending' | 'synced' | 'attendance_unavailable' | 'sync_failed';

export interface MeetingAttendanceResponse {
  available: boolean;
  attendees: readonly MeetingAttendeeResponse[];
  syncStatus?: MeetingAttendanceSyncStatus;
}
```

Replace the component's template and add a `syncStatus` signal:

```ts
  template: `
    @if (available()) {
      @if (attendees().length === 0) {
        <p data-testid="calendar-attendance-empty" class="text-xs text-[var(--color-text-secondary)]">Nobody joined this meeting.</p>
      } @else {
        <div class="flex flex-col gap-1">
          @for (attendee of attendees(); track attendee.email ?? attendee.name ?? attendee.joinedAt) {
            <div data-testid="calendar-attendance-row" class="flex items-center justify-between text-xs">
              <span class="truncate">{{ attendee.name ?? attendee.email ?? 'Unknown participant' }}</span>
              <span class="shrink-0 text-[var(--color-text-secondary)]">{{ formatDuration(attendee.durationSeconds) }}</span>
            </div>
          }
        </div>
      }
    } @else {
      <p data-testid="calendar-attendance-unavailable" class="text-xs text-[var(--color-text-secondary)]">
        {{ unavailableMessage() }}
      </p>
    }
  `,
```

```ts
  protected readonly syncStatus = signal<string>('pending');

  protected unavailableMessage(): string {
    switch (this.syncStatus()) {
      case 'attendance_unavailable':
        return "The meeting provider didn't send attendance data for this meeting.";
      case 'sync_failed':
        return "We couldn't fetch attendance from the meeting provider.";
      case 'none':
        return 'Attendance data unavailable';
      default:
        return 'Attendance appears after the meeting ends.';
    }
  }
```

and in `ngOnInit`'s subscribe add `this.syncStatus.set(response.syncStatus ?? 'pending');`.

- [ ] **Step 5: Run the frontend tests and type-check**

Run: `npx ng test --watch=false --include src/app/modules/calendar/ui/calendar-event-attendance/calendar-event-attendance.component.spec.ts` → Expected: all pass.
Run: `npx ng build --configuration development` → Expected: success.

- [ ] **Step 6: Commit (both repos)**

```bash
cd "C:/Users/User/onexso-worktrees/be-meeting-task"
git add src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs tests/ONEVO.Tests.Unit/Features/Calendar/GetEventMeetingAttendanceQueryHandlerTests.cs
git commit -m "feat(calendar-attendance): expose the attendance sync status

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
cd "C:/Users/User/onexso-worktrees/fe-meeting-task"
git add src/app/modules/calendar/models/calendar-event.model.ts src/app/modules/calendar/ui/calendar-event-attendance
git commit -m "feat(calendar-attendance): say whether attendance is pending, unavailable, failed or empty

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Verification and push

- [ ] **Step 1:** `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo` → 0 errors. Unit and architecture suites → `Failed: 0`.
- [ ] **Step 2:** `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~Calendar"` → `Failed: 0`.
- [ ] **Step 3:** Frontend `npx ng test --watch=false` → only the pre-existing date-dependent `task-form-modal` failure (documented in the Phase 1 closure report §4).
- [ ] **Step 4:** `git push origin feature/meeting-task-phase0-1` in both worktrees (no PR).
