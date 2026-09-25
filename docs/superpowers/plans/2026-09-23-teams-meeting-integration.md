# Microsoft Teams Meeting Integration (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a calendar-event organizer auto-generate a real Microsoft Teams meeting (join link
populates `calendar_events.MeetingLink`), and record who joined/for how long once the meeting has
happened.

**Architecture:** Standard MediatR CQRS (Domain/Application/Infrastructure/Api), tenant-scoped via
`TenantPolicy` + RLS, reusing the existing per-employee `external_calendar_connections` OAuth
model built for Google/Outlook calendar sync (PR #117) rather than a parallel connection system.

**Tech Stack:** .NET 10 / EF Core / PostgreSQL (RLS), MediatR, `IEncryptionService` (token-at-rest,
already exists), `BackgroundService` + `PeriodicTimer` (attendance polling job, mirrors
`CalendarSyncJob`). Frontend: Angular standalone components/signals, existing
`CalendarConnectionApiService`/`calendar-connections` settings screen (already built — this plan
does not touch it beyond extending the OAuth scope it requests).

**Spec:** `docs/superpowers/specs/2026-09-23-teams-zoom-meeting-integration-design.md`

## Global Constraints

- Tenant-side routes: `api/v1/calendar/...`, `[Authorize(Policy = "TenantPolicy")]`, reuse
  `calendar:read`/`calendar:write` permission codes — no new permissions to seed.
- New encrypted/sensitive fields (none needed here — this plan adds no new secrets; it reads the
  *existing* `external_calendar_connections.AccessTokenEncrypted`/`RefreshTokenEncrypted` via the
  already-built `IEncryptionService`).
- Snake_case DB columns via the existing EF `UseSnakeCaseNamingConvention()` — entity properties
  stay PascalCase.
- No Hangfire — background job shape mirrors `CalendarSyncJob`.
- No new frontend "Connections" UI — Teams meeting creation reuses the connection an employee
  already made (or will make) via the *existing* Microsoft "Connect" button under Settings →
  Calendar Connections (`calendar-connections.component.ts`); this plan only changes which scopes
  that button requests.
- Every task's DI registration, repository, and controller action must follow the exact existing
  patterns cited inline in that task — this codebase has one established shape per layer and
  departing from it is not a stylistic choice a task should make on its own.

---

## Task 1: Domain entities

**Files:**
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs`
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeetingAttendance.cs`

**Interfaces:**
- Produces: `CalendarEventMeeting`, `CalendarEventMeetingAttendance`, `CalendarEventMeetingProviders`
  (constants: `MicrosoftTeams = "microsoft_teams"`, `Zoom = "zoom"`), `CalendarEventMeetingStatuses`
  (`Active = "active"`, `Cancelled = "cancelled"`, `Failed = "failed"`) — every later task's code
  uses these exact type/constant names.

- [ ] **Step 1: Write `CalendarEventMeeting`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventMeetingProviders
{
    public const string MicrosoftTeams = "microsoft_teams";
    public const string Zoom = "zoom"; // unused until Phase 2 - reserved value, not yet writable
}

public static class CalendarEventMeetingStatuses
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public class CalendarEventMeeting : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid ExternalCalendarConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalMeetingId { get; set; } = string.Empty;
    public string JoinUrl { get; set; } = string.Empty;
    public string? OrganizerJoinUrl { get; set; }
    public string? PasscodeOrPin { get; set; }
    public string Status { get; set; } = CalendarEventMeetingStatuses.Active;
    public DateTimeOffset? LastAttendanceSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write `CalendarEventMeetingAttendance`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public class CalendarEventMeetingAttendance : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventMeetingId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? ExternalParticipantName { get; set; }
    public string? ExternalParticipantEmail { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 3: Build to confirm both compile**

Run: `dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj --nologo`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeetingAttendance.cs
git commit -m "feat(calendar): add CalendarEventMeeting/Attendance domain entities"
```

---

## Task 2: EF configurations + migration

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventMeetingConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventMeetingAttendanceConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add two `DbSet<T>` properties, next to line 325's `ExternalCalendarEventLinks`)
- Create: migration via `dotnet ef migrations add AddCalendarEventMeetings` (generates `.cs`/`.Designer.cs`, updates `ApplicationDbContextModelSnapshot.cs`)

**Interfaces:**
- Consumes: `CalendarEventMeeting`, `CalendarEventMeetingAttendance` (Task 1).
- Produces: `db.CalendarEventMeetings`, `db.CalendarEventMeetingAttendances` — every repository in
  Task 3 uses these exact `DbSet` property names.

- [ ] **Step 1: Write `CalendarEventMeetingConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventMeetingConfiguration : IEntityTypeConfiguration<CalendarEventMeeting>
{
    public void Configure(EntityTypeBuilder<CalendarEventMeeting> builder)
    {
        builder.ToTable("calendar_event_meetings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Provider).HasMaxLength(30).IsRequired();
        builder.Property(m => m.ExternalMeetingId).HasMaxLength(255).IsRequired();
        builder.Property(m => m.JoinUrl).HasMaxLength(500).IsRequired();
        builder.Property(m => m.OrganizerJoinUrl).HasMaxLength(500);
        builder.Property(m => m.PasscodeOrPin).HasMaxLength(50);
        builder.Property(m => m.Status).HasMaxLength(20).IsRequired();

        // One auto-generated meeting per event.
        builder.HasIndex(m => m.CalendarEventId)
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_meetings_one_per_event");

        builder.HasIndex(m => new { m.TenantId, m.ExternalCalendarConnectionId })
            .HasDatabaseName("ix_calendar_event_meetings_tenant_id_connection_id");
    }
}
```

- [ ] **Step 2: Write `CalendarEventMeetingAttendanceConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventMeetingAttendanceConfiguration : IEntityTypeConfiguration<CalendarEventMeetingAttendance>
{
    public void Configure(EntityTypeBuilder<CalendarEventMeetingAttendance> builder)
    {
        builder.ToTable("calendar_event_meeting_attendances");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ExternalParticipantName).HasMaxLength(200);
        builder.Property(a => a.ExternalParticipantEmail).HasMaxLength(255);

        builder.HasIndex(a => new { a.TenantId, a.CalendarEventMeetingId })
            .HasDatabaseName("ix_calendar_event_meeting_attendances_tenant_id_meeting_id");
    }
}
```

- [ ] **Step 3: Register both DbSets**

In `ApplicationDbContext.cs`, immediately after the existing line
`public DbSet<ExternalCalendarEventLink> ExternalCalendarEventLinks => Set<ExternalCalendarEventLink>();`:

```csharp
    public DbSet<CalendarEventMeeting> CalendarEventMeetings => Set<CalendarEventMeeting>();
    public DbSet<CalendarEventMeetingAttendance> CalendarEventMeetingAttendances => Set<CalendarEventMeetingAttendance>();
```

- [ ] **Step 4: Export the MigrationConnection env var and generate the migration**

```bash
export ConnectionStrings__MigrationConnection="Host=$ONEVO_DB_HOST;Port=$ONEVO_DB_PORT;Database=$ONEVO_DB_NAME;Username=$ONEVO_DB_MIGRATOR_USER;Password=$ONEVO_DB_MIGRATOR_PASSWORD"
dotnet ef migrations add AddCalendarEventMeetings --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

(Stop the running `backend-api` preview first if one is active — it locks `ONEVO.Infrastructure.dll`.)

- [ ] **Step 5: Hand-edit the generated migration to add the two new tables' RLS policy**

Open the generated `<timestamp>_AddCalendarEventMeetings.cs`. EF Core's auto-generated `Up()`/`Down()`
already contain the `CreateTable`/`CreateIndex`/`DropTable` calls from the model diff — add the RLS
block to `Up()` (after the `CreateIndex` calls) and the matching teardown to `Down()` (before the
`DropTable` calls), following the exact pattern in
`src/ONEVO.Infrastructure/Migrations/20260908113010_AddExternalCalendarSync.cs`:

```csharp
        private static readonly string[] TenantTables = ["calendar_event_meetings", "calendar_event_meeting_attendances"];

        // ... inside Up(), after the CreateIndex calls EF generated:
        foreach (var table in TenantTables)
        {
            migrationBuilder.Sql($@"
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON {table};
                CREATE POLICY tenant_isolation ON {table}
                    USING (
                        current_setting('app.tenant_context_mode', true) = 'admin'
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    )
                    WITH CHECK (
                        current_setting('app.tenant_context_mode', true) = 'admin'
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    );
            ");
        }

        // ... inside Down(), before the DropTable calls EF generated:
        foreach (var table in TenantTables)
        {
            migrationBuilder.Sql($@"
                DROP POLICY IF EXISTS tenant_isolation ON {table};
                ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
            ");
        }
```

- [ ] **Step 6: Apply the migration and verify with a drift check**

```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
dotnet ef migrations add ZzDriftCheck --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: `ZzDriftCheck`'s generated `Up()`/`Down()` are both empty (proves the hand-added RLS SQL
didn't drift the model). Delete the throwaway file:

```bash
rm src/ONEVO.Infrastructure/Migrations/*_ZzDriftCheck*
```

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventMeetingConfiguration.cs src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventMeetingAttendanceConfiguration.cs src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(calendar): add calendar_event_meetings/attendances tables + RLS"
```

---

## Task 3: Repository interfaces + EF implementations

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs`
- Create: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingAttendanceRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingAttendanceRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register both, next to line 352's `IExternalCalendarEventLinkRepository` registration)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingRepositoryTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingAttendanceRepositoryTests.cs`

**Interfaces:**
- Consumes: `CalendarEventMeeting`, `CalendarEventMeetingAttendance` (Task 1); `ApplicationDbContext.CalendarEventMeetings`/`CalendarEventMeetingAttendances` (Task 2).
- Produces: `ICalendarEventMeetingRepository`, `ICalendarEventMeetingAttendanceRepository` — Task 5's `CreateEventMeetingCommandHandler`/`RemoveEventMeetingCommandHandler`/`TeamsAttendanceSyncJob` depend on these exact method signatures.

- [ ] **Step 1: Write the failing repository tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfCalendarEventMeetingRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetTrackedByCalendarEventAsync_ReturnsMatchingMeeting()
    {
        await using var db = BuildInMemoryDb();
        var eventId = Guid.NewGuid();
        var meeting = MakeMeeting(eventId);
        db.CalendarEventMeetings.Add(meeting);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventMeetingRepository(db);
        var result = await repository.GetTrackedByCalendarEventAsync(TenantId, eventId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(meeting.Id, result!.Id);
    }

    [Fact]
    public async Task GetTrackedByCalendarEventAsync_NoMeeting_ReturnsNull()
    {
        await using var db = BuildInMemoryDb();

        var repository = new EfCalendarEventMeetingRepository(db);
        var result = await repository.GetTrackedByCalendarEventAsync(TenantId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDueForAttendanceSyncAsync_ReturnsOnlyActiveUnsyncedMeetingsPastEndDate()
    {
        await using var db = BuildInMemoryDb();
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        var due = MakeMeeting(Guid.NewGuid());
        var alreadySynced = MakeMeeting(Guid.NewGuid());
        alreadySynced.LastAttendanceSyncedAt = now.AddMinutes(-5);
        var cancelled = MakeMeeting(Guid.NewGuid());
        cancelled.Status = CalendarEventMeetingStatuses.Cancelled;

        db.CalendarEventMeetings.AddRange(due, alreadySynced, cancelled);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventMeetingRepository(db);
        var result = await repository.GetDueForAttendanceSyncAsync(TenantId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(due.Id, result[0].Id);
    }

    private static CalendarEventMeeting MakeMeeting(Guid calendarEventId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = calendarEventId,
        ExternalCalendarConnectionId = Guid.NewGuid(), Provider = CalendarEventMeetingProviders.MicrosoftTeams,
        ExternalMeetingId = "graph-meeting-1", JoinUrl = "https://teams.microsoft.com/l/meetup-join/abc",
        Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ONEVO.Application.Common.ServiceInterfaces.ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingAttendanceRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfCalendarEventMeetingAttendanceRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetByMeetingIdAsync_ReturnsOnlyThatMeetingsAttendances()
    {
        await using var db = BuildInMemoryDb();
        var meetingId1 = Guid.NewGuid();
        var meetingId2 = Guid.NewGuid();
        db.CalendarEventMeetingAttendances.AddRange(
            MakeAttendance(meetingId1), MakeAttendance(meetingId1), MakeAttendance(meetingId2));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventMeetingAttendanceRepository(db);
        var result = await repository.GetByMeetingIdAsync(TenantId, meetingId1, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal(meetingId1, a.CalendarEventMeetingId));
    }

    private static CalendarEventMeetingAttendance MakeAttendance(Guid meetingId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventMeetingId = meetingId,
        ExternalParticipantEmail = "person@example.com", JoinedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ONEVO.Application.Common.ServiceInterfaces.ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (types don't exist yet)**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfCalendarEventMeeting" --nologo`
Expected: build FAILs (`EfCalendarEventMeetingRepository`/`EfCalendarEventMeetingAttendanceRepository` don't exist).

- [ ] **Step 3: Write the repository interfaces**

```csharp
// src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventMeetingRepository
{
    Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default);
    Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>Active meetings whose event has already ended and whose attendance has never
    /// been synced - TeamsAttendanceSyncJob's poll target.</summary>
    Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, CancellationToken ct = default);

    void Update(CalendarEventMeeting meeting);
}
```

```csharp
// src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingAttendanceRepository.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventMeetingAttendanceRepository
{
    Task AddRangeAsync(IEnumerable<CalendarEventMeetingAttendance> attendances, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventMeetingAttendance>> GetByMeetingIdAsync(Guid tenantId, Guid calendarEventMeetingId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the EF implementations**

`GetDueForAttendanceSyncAsync` needs the parent event's `EndDate`, which lives on `CalendarEvent`,
not on `CalendarEventMeeting` itself — join across `db.CalendarEvents` (existing `DbSet` this
context already exposes for the Calendar module).

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventMeetingRepository : ICalendarEventMeetingRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventMeetingRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AddAsync(meeting, ct);

    public async Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.CalendarEventId == calendarEventId, ct);

    public async Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await (
            from meeting in _db.CalendarEventMeetings.AsNoTracking()
            join calendarEvent in _db.CalendarEvents.AsNoTracking() on meeting.CalendarEventId equals calendarEvent.Id
            where meeting.TenantId == tenantId
                && meeting.Status == CalendarEventMeetingStatuses.Active
                && meeting.LastAttendanceSyncedAt == null
                && calendarEvent.EndDate < now
            select meeting
        ).ToListAsync(ct);
    }

    public void Update(CalendarEventMeeting meeting) => _db.CalendarEventMeetings.Update(meeting);
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingAttendanceRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventMeetingAttendanceRepository : ICalendarEventMeetingAttendanceRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventMeetingAttendanceRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<CalendarEventMeetingAttendance> attendances, CancellationToken ct = default)
        => await _db.CalendarEventMeetingAttendances.AddRangeAsync(attendances, ct);

    public async Task<IReadOnlyList<CalendarEventMeetingAttendance>> GetByMeetingIdAsync(Guid tenantId, Guid calendarEventMeetingId, CancellationToken ct = default)
        => await _db.CalendarEventMeetingAttendances.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.CalendarEventMeetingId == calendarEventMeetingId)
            .OrderBy(a => a.JoinedAt)
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Register both in DI**

In `DependencyInjection.cs`, immediately after line 352's
`services.AddScoped<IExternalCalendarEventLinkRepository, EfExternalCalendarEventLinkRepository>();`:

```csharp
        services.AddScoped<ICalendarEventMeetingRepository, EfCalendarEventMeetingRepository>();
        services.AddScoped<ICalendarEventMeetingAttendanceRepository, EfCalendarEventMeetingAttendanceRepository>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfCalendarEventMeeting" --nologo`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingAttendanceRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingAttendanceRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingRepositoryTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingAttendanceRepositoryTests.cs
git commit -m "feat(calendar): add CalendarEventMeeting/Attendance repositories"
```

---

## Task 4: Extract shared connection-token-refresh service (DRY prerequisite)

`CalendarSyncService.EnsureFreshAccessTokenAsync` (private method,
`src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs:87-117`) is the only place this
codebase currently knows how to turn an `ExternalCalendarConnection` into a valid, non-expired
access token (decrypt-or-refresh, persist the refreshed token, mark `ReauthRequired` on failure).
Task 5's command handler and Task 7's background job both need this exact same logic — duplicating
it a second and third time is exactly the kind of copy-paste this plan's own design rejected when
choosing to extend the existing connection table instead of forking one. Extract it once, here,
before either later task needs it.

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarConnectionTokenProvider.cs`
- Create: `src/ONEVO.Infrastructure/Services/Calendar/CalendarConnectionTokenProvider.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs` (delete
  `EnsureFreshAccessTokenAsync`, call the new service instead)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register the new service)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarConnectionTokenProviderTests.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceTests.cs` (existing token-refresh-path tests move to the new test file; `CalendarSyncServiceTests.cs` mocks `ICalendarConnectionTokenProvider` going forward instead of the app resolver/token-exchange client directly for that concern)

**Interfaces:**
- Consumes: `ExternalCalendarConnection` (existing), `IPlatformOAuthAppResolver`,
  `ICalendarOAuthTokenExchangeClient`, `IEncryptionService`, `IUnitOfWork`,
  `IExternalCalendarConnectionRepository` (all existing).
- Produces: `ICalendarConnectionTokenProvider.GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct) -> Task<string?>` — Tasks 5 and 7 call this exact signature.

- [ ] **Step 1: Write the failing test for the extracted service**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CalendarConnectionTokenProviderTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarConnectionTokenProviderTests
{
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarOAuthTokenExchangeClient> _tokenExchange = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CalendarConnectionTokenProvider BuildSut() => new(
        _connections.Object, _tokenExchange.Object, _appResolver.Object, _encryption.Object, _unitOfWork.Object,
        Mock.Of<Microsoft.Extensions.Logging.ILogger<CalendarConnectionTokenProvider>>());

    [Fact]
    public async Task GetFreshAccessTokenAsync_TokenNotExpired_ReturnsDecryptedTokenWithoutRefreshing()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), AccessTokenEncrypted = [1, 2, 3],
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        _encryption.Setup(e => e.DecryptBytes(connection.AccessTokenEncrypted!)).Returns("valid-token");

        var result = await BuildSut().GetFreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Equal("valid-token", result);
        _tokenExchange.Verify(t => t.RefreshTokenAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetFreshAccessTokenAsync_TokenExpired_RefreshesAndPersists()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), RefreshTokenEncrypted = [9, 9, 9],
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        _appResolver.Setup(a => a.GetActiveAppForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformOAuthAppRuntime("microsoft", "https://login.microsoftonline.com/common/oauth2/v2.0/token"));
        _appResolver.Setup(a => a.GetActiveCredentialForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformOAuthAppCredential("client-id", "client-secret"));
        _encryption.Setup(e => e.DecryptBytes(connection.RefreshTokenEncrypted)).Returns("old-refresh-token");
        _tokenExchange.Setup(t => t.RefreshTokenAsync(
                "https://login.microsoftonline.com/common/oauth2/v2.0/token", "client-id", "client-secret", "old-refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderTokens("new-access-token", "new-refresh-token", DateTimeOffset.UtcNow.AddHours(1)));
        _encryption.Setup(e => e.EncryptBytes("new-access-token")).Returns([1]);
        _encryption.Setup(e => e.EncryptBytes("new-refresh-token")).Returns([2]);

        var result = await BuildSut().GetFreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Equal("new-access-token", result);
        _connections.Verify(c => c.Update(connection), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFreshAccessTokenAsync_RefreshFails_MarksReauthRequiredAndReturnsNull()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), RefreshTokenEncrypted = [9], ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        _appResolver.Setup(a => a.GetActiveAppForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthAppRuntime?)null);

        var result = await BuildSut().GetFreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(ExternalCalendarConnectionStatuses.ReauthRequired, connection.Status);
        _connections.Verify(c => c.Update(connection), Times.Once);
    }
}
```

*(`PlatformOAuthAppRuntime`/`PlatformOAuthAppCredential`/`CalendarProviderTokens` are the existing
types `IPlatformOAuthAppResolver`/`ICalendarOAuthTokenExchangeClient` already return — check their
exact existing constructor shape in
`src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/ServiceInterfaces/IPlatformOAuthAppResolver.cs`
and `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarOAuthTokenExchangeClient.cs`
before writing this step for real — adjust the test's constructor calls to match if the actual
positional-parameter order differs from what's assumed above.)*

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarConnectionTokenProviderTests" --nologo`
Expected: build fails (`CalendarConnectionTokenProvider` doesn't exist yet).

- [ ] **Step 3: Write the interface**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarConnectionTokenProvider.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Returns a valid (non-expired) access token for an external calendar connection,
/// refreshing and persisting a new one if needed. Returns null and marks the connection
/// ReauthRequired if the refresh itself fails - callers must treat null as "cannot proceed",
/// not retry immediately.</summary>
public interface ICalendarConnectionTokenProvider
{
    Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct);
}
```

- [ ] **Step 4: Write the implementation** — move `EnsureFreshAccessTokenAsync`'s body out of
  `CalendarSyncService` verbatim (constructor deps: `IExternalCalendarConnectionRepository`,
  `ICalendarOAuthTokenExchangeClient`, `IPlatformOAuthAppResolver`, `IEncryptionService`,
  `IUnitOfWork`, `ILogger<CalendarConnectionTokenProvider>`), renamed to the interface method name
  and made `public`:

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/CalendarConnectionTokenProvider.cs
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class CalendarConnectionTokenProvider(
    IExternalCalendarConnectionRepository connections,
    ICalendarOAuthTokenExchangeClient tokenExchangeClient,
    IPlatformOAuthAppResolver appResolver,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    ILogger<CalendarConnectionTokenProvider> logger)
    : ICalendarConnectionTokenProvider
{
    public async Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
    {
        var needsRefresh = connection.ExpiresAt is null || connection.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!needsRefresh && connection.AccessTokenEncrypted is not null)
            return encryption.DecryptBytes(connection.AccessTokenEncrypted);

        try
        {
            var app = await appResolver.GetActiveAppForProviderAsync(oauthProvider, ct);
            var credential = await appResolver.GetActiveCredentialForProviderAsync(oauthProvider, ct);
            if (app is null || credential is null)
                throw new InvalidOperationException($"No active OAuth app configured for provider '{oauthProvider}'.");

            var refreshToken = encryption.DecryptBytes(connection.RefreshTokenEncrypted);
            var tokens = await tokenExchangeClient.RefreshTokenAsync(app.TokenUrl, credential.ClientId, credential.ClientSecret, refreshToken, ct);

            connection.AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken);
            connection.RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken ?? refreshToken);
            connection.ExpiresAt = tokens.ExpiresAt;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return tokens.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh failed for connection {ConnectionId}; marking reauth_required.", connection.Id);
            connection.Status = ExternalCalendarConnectionStatuses.ReauthRequired;
            connection.LastError = ex.Message;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }
    }
}
```

- [ ] **Step 5: Update `CalendarSyncService` to call the extracted service instead**

In `CalendarSyncService.cs`: add `ICalendarConnectionTokenProvider tokenProvider` to the primary
constructor's parameter list; delete the private `EnsureFreshAccessTokenAsync` method entirely;
change the call site (currently `var accessToken = await EnsureFreshAccessTokenAsync(connection, oauthProvider, ct);`)
to `var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);`.
Remove `IExternalCalendarConnectionRepository connections`, `ICalendarOAuthTokenExchangeClient
tokenExchangeClient`, `IPlatformOAuthAppResolver appResolver` from the constructor **only if**
nothing else in the class still uses them directly — check the rest of the file (`connections` is
still referenced by `connections.GetTrackedByIdForTenantAsync` at the top of
`SyncConnectionAsync` and `connections.Update(connection)` at the bottom of the same method, so
keep it; `tokenExchangeClient`/`appResolver` become unused and should be removed from the
constructor along with their `using` if nothing else references them).

- [ ] **Step 6: Move the existing token-refresh-path tests out of `CalendarSyncServiceTests.cs`**

Open `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceTests.cs`: any test asserting
refresh/expiry/reauth behavior belongs in the new `CalendarConnectionTokenProviderTests.cs` now
(delete the duplicated assertions here) — `CalendarSyncServiceTests.cs`'s `BuildSut()` helper
instead mocks `ICalendarConnectionTokenProvider.GetFreshAccessTokenAsync(...)` to return a canned
token directly, since token-refresh mechanics are no longer this class's concern to test.

- [ ] **Step 7: Register the new service in DI**

In `DependencyInjection.cs`, immediately before the existing `ICalendarSyncService` registration:

```csharp
        services.AddScoped<ICalendarConnectionTokenProvider, CalendarConnectionTokenProvider>();
```

- [ ] **Step 8: Run the full Calendar test suite to confirm no regression**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Features.Calendar" --nologo`
Expected: all pass, including the new `CalendarConnectionTokenProviderTests`.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarConnectionTokenProvider.cs src/ONEVO.Infrastructure/Services/Calendar/CalendarConnectionTokenProvider.cs src/ONEVO.Infrastructure/Services/Calendar/CalendarSyncService.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarConnectionTokenProviderTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarSyncServiceTests.cs
git commit -m "refactor(calendar): extract connection token refresh into its own service"
```

---

## Task 5: `ITeamsMeetingClient` + `MicrosoftGraphMeetingClient`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs`
- Create: `src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register, next to line 500's
  `IMicrosoftGraphCalendarClient` registration)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs`

**Interfaces:**
- Produces: `ITeamsMeetingClient`, `TeamsMeetingDto`, `TeamsAttendanceRecordDto` — Task 6's command
  handlers and Task 7's job depend on these exact shapes.

- [ ] **Step 1: Write the failing client tests** (mirrors `MicrosoftGraphCalendarClientTests.cs`'s
  `HttpMessageHandler`-mocking pattern — read that file first for the exact mock-handler helper it
  uses, then reuse it here rather than re-deriving it)

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs
using System.Net;
using System.Net.Http.Json;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class MicrosoftGraphMeetingClientTests
{
    [Fact]
    public async Task CreateMeetingAsync_PostsToOnlineMeetingsEndpoint_ReturnsJoinInfo()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    id = "graph-meeting-1",
                    joinWebUrl = "https://teams.microsoft.com/l/meetup-join/abc",
                    joinInformation = new { content = "Passcode: 123456" }
                })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler));

        var result = await client.CreateMeetingAsync(
            "access-token", "Sprint planning", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);

        Assert.Equal("graph-meeting-1", result.ExternalMeetingId);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", result.JoinUrl);
    }

    [Fact]
    public async Task CancelMeetingAsync_SendsDeleteToTheMeetingsExternalId()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler));

        await client.CancelMeetingAsync("access-token", "graph-meeting-1", CancellationToken.None);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
```

*(If `MicrosoftGraphCalendarClientTests.cs` already defines a shared fake-handler helper in a
common test-support file, reuse that one instead of redefining `FakeHttpMessageHandler` here —
check before writing this step for real.)*

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~MicrosoftGraphMeetingClientTests" --nologo`

- [ ] **Step 3: Write the interface**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record TeamsMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record TeamsAttendanceRecordDto(
    string? ParticipantName, string? ParticipantEmail, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public interface ITeamsMeetingClient
{
    Task<TeamsMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct);
}
```

- [ ] **Step 4: Write the implementation**

```csharp
// src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphMeetingClient(HttpClient httpClient) : ITeamsMeetingClient
{
    public async Task<TeamsMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/onlineMeetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            startDateTime = start.UtcDateTime.ToString("o"),
            endDateTime = end.UtcDateTime.ToString("o"),
            subject
        });
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var externalMeetingId = root.GetProperty("id").GetString()!;
        var joinUrl = root.GetProperty("joinWebUrl").GetString()!;
        string? passcode = null;
        if (root.TryGetProperty("joinInformation", out var joinInfo)
            && joinInfo.TryGetProperty("content", out var content))
        {
            passcode = content.GetString();
        }

        return new TeamsMeetingDto(externalMeetingId, joinUrl, OrganizerJoinUrl: null, passcode);
    }

    public async Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // Graph returns a list of attendance *reports* per meeting (one per session, for a
        // recurring/reconvened meeting); Phase 1 events are non-recurring single sessions, so take
        // the most recent report only.
        using var reportsRequest = new HttpRequestMessage(
            HttpMethod.Get, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports");
        reportsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var reportsResponse = await httpClient.SendAsync(reportsRequest, ct);
        reportsResponse.EnsureSuccessStatusCode();
        using var reportsStream = await reportsResponse.Content.ReadAsStreamAsync(ct);
        using var reportsDoc = await JsonDocument.ParseAsync(reportsStream, cancellationToken: ct);
        var reports = reportsDoc.RootElement.GetProperty("value").EnumerateArray().ToList();
        if (reports.Count == 0)
            return [];
        var reportId = reports[^1].GetProperty("id").GetString();

        using var recordsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports/{reportId}/attendanceRecords");
        recordsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var recordsResponse = await httpClient.SendAsync(recordsRequest, ct);
        recordsResponse.EnsureSuccessStatusCode();
        using var recordsStream = await recordsResponse.Content.ReadAsStreamAsync(ct);
        using var recordsDoc = await JsonDocument.ParseAsync(recordsStream, cancellationToken: ct);

        var result = new List<TeamsAttendanceRecordDto>();
        foreach (var record in recordsDoc.RootElement.GetProperty("value").EnumerateArray())
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
        return result;
    }
}
```

- [ ] **Step 5: Register in DI**

In `DependencyInjection.cs`, immediately after line 500's
`services.AddHttpClient<IMicrosoftGraphCalendarClient, MicrosoftGraphCalendarClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });`:

```csharp
        services.AddHttpClient<ITeamsMeetingClient, MicrosoftGraphMeetingClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~MicrosoftGraphMeetingClientTests" --nologo`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs
git commit -m "feat(calendar): add ITeamsMeetingClient / MicrosoftGraphMeetingClient"
```

---

## Task 6: Extend `PlatformOAuthProviderCatalog`'s Microsoft scope

**Files:**
- Modify: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Helpers/PlatformOAuthProviderCatalog.cs`
- Modify: any existing test asserting the exact `microsoft` `DefaultScopes` array (search first: `grep -rn "Calendars.ReadWrite" tests/`)

**Interfaces:**
- Consumes: nothing new.
- Produces: employees who (re)connect Microsoft via the existing Settings → Calendar Connections
  screen now receive `OnlineMeetings.ReadWrite` in their granted scope, which Task 8's
  `CreateEventMeetingCommandHandler` checks for.

- [ ] **Step 1: Find and update the existing scope-assertion test(s)**

```bash
grep -rn "Calendars.ReadWrite" tests/
```

Update whatever test(s) that finds to expect `OnlineMeetings.ReadWrite` in the `microsoft` entry's
`DefaultScopes` array alongside the existing five scopes.

- [ ] **Step 2: Run that test to verify it now fails against the unchanged catalog**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PlatformOAuthProviderCatalog" --nologo`

- [ ] **Step 3: Update the catalog**

In `PlatformOAuthProviderCatalog.cs`, add the new capability constant next to the existing three:

```csharp
    public const string CapabilityMeetings = "meetings";
```

Change the `microsoft` entry's `DefaultScopes` and `Capabilities`:

```csharp
                DefaultScopes: new[] { "openid", "profile", "email", "offline_access", "User.Read", "Calendars.ReadWrite", "OnlineMeetings.ReadWrite" },
                ClientSecretRequired: true,
                Capabilities: new[] { CapabilityUserOAuth, CapabilityCalendar, CapabilityMeetings }),
```

(Leave the `zoom` entry untouched — its scope correction is explicitly Phase 2, per the spec.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PlatformOAuthProviderCatalog" --nologo`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Helpers/PlatformOAuthProviderCatalog.cs
git commit -am "feat(calendar): request OnlineMeetings.ReadWrite scope for Microsoft connections"
```

---

## Task 7: `CreateEventMeetingCommand` + Handler

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository` (existing, `GetTrackedByIdForTenantAsync`),
  `IExternalCalendarConnectionRepository.GetByTenantUserProviderAsync` (existing),
  `ICalendarConnectionTokenProvider.GetFreshAccessTokenAsync` (Task 4), `ITeamsMeetingClient` (Task 5),
  `ICalendarEventMeetingRepository` (Task 3), `IUnitOfWork` (existing).
- Produces: `CreateEventMeetingCommand(Guid EventId)`, handler returning
  `Result<CreateEventMeetingResult>` where `CreateEventMeetingResult(string JoinUrl)` — Task 9's
  controller action calls this exact command/result shape.

- [ ] **Step 1: Write the failing handler tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokenProvider = new();
    private readonly Mock<ITeamsMeetingClient> _teamsClient = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CreateEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<object>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<object>>, CancellationToken>((op, ct) => op(ct));
        return new CreateEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _connections.Object, _tokenProvider.Object,
            _teamsClient.Object, _meetings.Object, _unitOfWork.Object);
    }

    private CalendarEvent MakeEvent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Sprint planning", CreatedById = UserId,
        StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2)
    };

    [Fact]
    public async Task Handle_NoMicrosoftConnection_ReturnsMeetingProviderNotConnectedConflict()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEvent());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("meeting_provider_not_connected");
    }

    [Fact]
    public async Task Handle_ConnectionMissingMeetingScope_ReturnsMeetingProviderNotConnectedConflict()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEvent());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
                ScopesJson = "[\"Calendars.ReadWrite\"]" // no OnlineMeetings.ReadWrite
            });

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("meeting_provider_not_connected");
    }

    [Fact]
    public async Task Handle_ValidConnection_CreatesTeamsMeetingAndSetsMeetingLink()
    {
        var evt = MakeEvent();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"Calendars.ReadWrite\",\"OnlineMeetings.ReadWrite\"]"
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");
        _teamsClient.Setup(t => t.CreateMeetingAsync("access-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsMeetingDto("graph-meeting-1", "https://teams.microsoft.com/l/meetup-join/abc", null, "123456"));

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.JoinUrl.Should().Be("https://teams.microsoft.com/l/meetup-join/abc");
        evt.MeetingLink.Should().Be("https://teams.microsoft.com/l/meetup-join/abc");
        _meetings.Verify(m => m.AddAsync(
            It.Is<CalendarEventMeeting>(cm => cm.ExternalMeetingId == "graph-meeting-1" && cm.CalendarEventId == EventId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var evt = MakeEvent();
        evt.CreatedById = Guid.NewGuid(); // someone else
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
```

*(Confirm `CalendarEvent`'s real namespace before writing this step for real — the working
codebase currently has two similarly-named `ICalendarEventRepository` interfaces in different
namespaces per the DI registration seen in `DependencyInjection.cs:339` and `:349`
(`WorkManagement.CalendarEvents` vs `Calendar`); this plan's `CreateEventMeetingCommand` operates
on the `Calendar` module's own `ICalendarEventRepository`/`CalendarEvent` — verify the entity's
actual namespace with `grep -rn "class CalendarEvent " src/ONEVO.Domain` before writing this task
for real, and correct the `using` above if it differs from what's assumed here.)*

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CreateEventMeetingCommandHandlerTests" --nologo`

- [ ] **Step 3: Write the command + result DTO**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed record CreateEventMeetingResult(string JoinUrl);

public sealed record CreateEventMeetingCommand(Guid EventId) : IRequest<Result<CreateEventMeetingResult>>;
```

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed class CreateEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    ICalendarEventMeetingRepository meetings,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateEventMeetingCommand, Result<CreateEventMeetingResult>>
{
    public async Task<Result<CreateEventMeetingResult>> Handle(CreateEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CreateEventMeetingResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CreateEventMeetingResult>.Forbidden("Only the event organizer can add a meeting.");

        var connection = await connections.GetByTenantUserProviderAsync(tenantId, currentUser.UserId, "outlook_calendar", ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson))
        {
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
        if (accessToken is null)
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");

        var meetingDto = await teamsClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.MeetingLink = meetingDto.JoinUrl;
            events.Update(existing);

            await meetings.AddAsync(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
                ExternalMeetingId = meetingDto.ExternalMeetingId, JoinUrl = meetingDto.JoinUrl,
                OrganizerJoinUrl = meetingDto.OrganizerJoinUrl, PasscodeOrPin = meetingDto.PasscodeOrPin,
                Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
            }, innerCt);

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(meetingDto.JoinUrl));
        }, ct);
    }

    private static bool HasMeetingScope(string scopesJson)
    {
        try
        {
            var scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? [];
            return scopes.Contains("OnlineMeetings.ReadWrite", StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

*(Verify `ICalendarEventRepository.Update`'s existing signature accepts the tracked entity
directly, matching `UpdateCalendarEventCommandHandler`'s `events.Update(existing);` call seen
above — the handler here follows that exact same call shape.)*

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CreateEventMeetingCommandHandlerTests" --nologo`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/ tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs
git commit -m "feat(calendar): add CreateEventMeetingCommand"
```

---

## Task 8: `RemoveEventMeetingCommand` + Handler (+ wire into event delete)

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommandHandler.cs`
- Modify: the existing delete-calendar-event command handler (find it: `grep -rln "class DeleteCalendarEventCommandHandler" src/`) — call `ITeamsMeetingClient.CancelMeetingAsync` when a `CalendarEventMeeting` exists for the event being deleted, before removing the event.
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs`
- Test: add one case to the existing delete-event handler's test file for the "has a Teams meeting" path.

**Interfaces:**
- Consumes: `ICalendarEventMeetingRepository` (Task 3), `IExternalCalendarConnectionRepository`,
  `ICalendarConnectionTokenProvider` (Task 4), `ITeamsMeetingClient` (Task 5).
- Produces: `RemoveEventMeetingCommand(Guid EventId)`, handler returning `Result` (no value).

- [ ] **Step 1: Write the failing handler test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RemoveEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokenProvider = new();
    private readonly Mock<ITeamsMeetingClient> _teamsClient = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private RemoveEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new RemoveEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _meetings.Object, _connections.Object,
            _tokenProvider.Object, _teamsClient.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_NoMeetingOnEvent_ReturnsSuccessNoOp()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.Calendar.Entities.CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId });
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);

        var result = await BuildSut().Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _teamsClient.Verify(t => t.CancelMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HasMeeting_CancelsRemotelyAndClearsMeetingLink()
    {
        var evt = new ONEVO.Domain.Features.Calendar.Entities.CalendarEvent
        {
            Id = EventId, TenantId = TenantId, CreatedById = UserId, MeetingLink = "https://teams.microsoft.com/l/meetup-join/abc"
        };
        var connection = new ExternalCalendarConnection { Id = Guid.NewGuid() };
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId,
            ExternalCalendarConnectionId = connection.Id, ExternalMeetingId = "graph-meeting-1",
            Status = CalendarEventMeetingStatuses.Active
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");

        var result = await BuildSut().Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        evt.MeetingLink.Should().BeNull();
        meeting.Status.Should().Be(CalendarEventMeetingStatuses.Cancelled);
        _teamsClient.Verify(t => t.CancelMeetingAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RemoveEventMeetingCommandHandlerTests" --nologo`

- [ ] **Step 3: Write the command**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;

public sealed record RemoveEventMeetingCommand(Guid EventId) : IRequest<Result>;
```

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;

public sealed class RemoveEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveEventMeetingCommand, Result>
{
    public async Task<Result> Handle(RemoveEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (existing is null)
            return Result.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event organizer can remove its meeting.");

        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, request.EventId, ct);
        if (meeting is null)
            return Result.Success(); // nothing to remove - not an error

        await CancelRemoteMeetingAsync(tenantId, meeting, ct);

        meeting.Status = CalendarEventMeetingStatuses.Cancelled;
        meetings.Update(meeting);
        existing.MeetingLink = null;
        events.Update(existing);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task CancelRemoteMeetingAsync(Guid tenantId, CalendarEventMeeting meeting, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return; // connection was disconnected since the meeting was created - nothing to cancel remotely

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
        if (accessToken is null)
            return; // reauth required - the remote meeting is orphaned but the local link is still cleared below

        await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
    }
}
```

- [ ] **Step 5: Wire into the existing delete-event flow**

```bash
grep -rln "class DeleteCalendarEventCommandHandler" src/ONEVO.Application/Features/Calendar/
```

Open that file. Inject `ICalendarEventMeetingRepository meetings`, `IExternalCalendarConnectionRepository connections`,
`ICalendarConnectionTokenProvider tokenProvider`, `ITeamsMeetingClient teamsClient` into its
primary constructor. At the start of its `Handle` method, immediately after the existing
organizer/not-found checks and before the event itself is removed, add:

```csharp
        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, ct);
        if (meeting is not null && meeting.Status == CalendarEventMeetingStatuses.Active)
        {
            var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
            if (connection is not null)
            {
                var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
                if (accessToken is not null)
                    await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
            }
        }
```

(Exact insertion point depends on that handler's real current structure — read the file first,
per Step 5's own instruction, before placing this block; it must run before the transaction that
deletes the `CalendarEvent` row commits, so a failure here still lets `SaveChangesAsync` roll the
whole delete back rather than leaving an orphaned remote meeting with no local event.)

- [ ] **Step 6: Add one test case to the existing delete-handler test file**

In whatever file `grep -rln "class DeleteCalendarEventCommandHandlerTests"` finds, add a test
mirroring `RemoveEventMeetingCommandHandlerTests.Handle_HasMeeting_CancelsRemotelyAndClearsMeetingLink`
above, asserting `teamsClient.CancelMeetingAsync` is called once when the deleted event had an
active `CalendarEventMeeting`, and never called when it didn't.

- [ ] **Step 7: Run to verify everything passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Calendar" --nologo`

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/ tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs
git add -u src/ONEVO.Application/Features/Calendar/Commands/  # picks up the modified delete-event handler + its test
git commit -m "feat(calendar): add RemoveEventMeetingCommand, wire into event delete"
```

---

## Task 9: Controller actions + `GetEventMeetingAttendanceQuery`

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs` (add view-model records)
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance/GetEventMeetingAttendanceQuery.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance/GetEventMeetingAttendanceQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/GetEventMeetingAttendanceQueryHandlerTests.cs`
- Test: architecture test — add this controller's three new actions to whichever existing
  `tests/ONEVO.Tests.Architecture/*.cs` file already asserts `[RequirePermission]` coverage for
  `CalendarController` (search: `grep -rln "CalendarController" tests/ONEVO.Tests.Architecture/`).

**Interfaces:**
- Consumes: `CreateEventMeetingCommand` (Task 7), `RemoveEventMeetingCommand` (Task 8),
  `ICalendarEventMeetingRepository`/`ICalendarEventMeetingAttendanceRepository` (Task 3).
- Produces: `POST /api/v1/calendar/{id}/meeting`, `DELETE /api/v1/calendar/{id}/meeting`,
  `GET /api/v1/calendar/{id}/meeting/attendance` — Task 12's frontend API service calls these
  exact routes.

- [ ] **Step 1: Write the query + handler's failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/GetEventMeetingAttendanceQueryHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetEventMeetingAttendanceQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<ICalendarEventMeetingAttendanceRepository> _attendances = new();

    private GetEventMeetingAttendanceQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        return new GetEventMeetingAttendanceQueryHandler(_currentUser.Object, _meetings.Object, _attendances.Object);
    }

    [Fact]
    public async Task Handle_NoMeeting_ReturnsEmptyUnavailableResult()
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Available.Should().BeFalse();
        result.Value.Attendees.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MeetingNeverSynced_ReturnsUnavailable()
    {
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = null });

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MeetingSynced_ReturnsAttendees()
    {
        var meeting = new CalendarEventMeeting { Id = Guid.NewGuid(), LastAttendanceSyncedAt = DateTimeOffset.UtcNow };
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(meeting);
        _attendances.Setup(a => a.GetByMeetingIdAsync(TenantId, meeting.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEventMeetingAttendance
                {
                    Id = Guid.NewGuid(), ExternalParticipantName = "Ada Lovelace", ExternalParticipantEmail = "ada@example.com",
                    JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-30), LeftAt = DateTimeOffset.UtcNow, DurationSeconds = 1800
                }
            ]);

        var result = await BuildSut().Handle(new GetEventMeetingAttendanceQuery(EventId), CancellationToken.None);

        result.Value!.Available.Should().BeTrue();
        result.Value.Attendees.Should().ContainSingle(a => a.Name == "Ada Lovelace");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetEventMeetingAttendanceQueryHandlerTests" --nologo`

- [ ] **Step 3: Write the query + DTOs**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance/GetEventMeetingAttendanceQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed record MeetingAttendeeItem(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, int? DurationSeconds);

public sealed record GetEventMeetingAttendanceResult(bool Available, IReadOnlyList<MeetingAttendeeItem> Attendees);

public sealed record GetEventMeetingAttendanceQuery(Guid EventId) : IRequest<Result<GetEventMeetingAttendanceResult>>;
```

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance/GetEventMeetingAttendanceQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed class GetEventMeetingAttendanceQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventMeetingRepository meetings,
    ICalendarEventMeetingAttendanceRepository attendances)
    : IRequestHandler<GetEventMeetingAttendanceQuery, Result<GetEventMeetingAttendanceResult>>
{
    public async Task<Result<GetEventMeetingAttendanceResult>> Handle(GetEventMeetingAttendanceQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetEventMeetingAttendanceResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, request.EventId, ct);
        if (meeting is null || meeting.LastAttendanceSyncedAt is null)
            return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(false, []));

        var records = await attendances.GetByMeetingIdAsync(tenantId, meeting.Id, ct);
        var items = records.Select(a => new MeetingAttendeeItem(
            a.ExternalParticipantName, a.ExternalParticipantEmail, a.JoinedAt, a.LeftAt, a.DurationSeconds)).ToList();
        return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(true, items));
    }
}
```

- [ ] **Step 5: Add the three controller actions**

Read `CalendarController.cs` first to match its exact existing style (constructor injection field
name `_mediator`, `Problem(result.Error, statusCode: result.StatusCode ?? 400)` pattern). Add,
near the existing `{id:guid}/respond` action:

```csharp
    [HttpPost("{id:guid}/meeting")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> CreateMeeting(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateEventMeetingCommand(id), ct);
        return result.IsSuccess
            ? Ok(new { joinUrl = result.Value!.JoinUrl })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}/meeting")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> RemoveMeeting(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveEventMeetingCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}/meeting/attendance")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> GetMeetingAttendance(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEventMeetingAttendanceQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the corresponding `using` statements for the three new command/query namespaces at the top of
the file, matching however the existing ones are grouped.

- [ ] **Step 6: Add the view-model + mapper in `CalendarContracts.cs`**

Follow whatever existing `ToViewModel()` extension-method pattern that file already uses for other
query results (read the file first):

```csharp
public sealed record MeetingAttendeeViewModel(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, int? DurationSeconds);
public sealed record MeetingAttendanceViewModel(bool Available, IReadOnlyList<MeetingAttendeeViewModel> Attendees);

public static MeetingAttendanceViewModel ToViewModel(this GetEventMeetingAttendanceResult result) =>
    new(result.Available, result.Attendees.Select(a => new MeetingAttendeeViewModel(a.Name, a.Email, a.JoinedAt, a.LeftAt, a.DurationSeconds)).ToList());
```

- [ ] **Step 7: Add the new actions to the existing controller permission-coverage architecture test**

Open whatever file `grep -rln "CalendarController" tests/ONEVO.Tests.Architecture/` finds and add
the three new actions if that test enumerates them by name rather than reflecting over all public
actions automatically (check which — most of this codebase's architecture tests reflect
automatically, in which case this step is just running them to confirm, not editing anything).

- [ ] **Step 8: Run the full Calendar + Architecture test suites**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Calendar" --nologo`
Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Application/Features/Calendar/Queries/GetEventMeetingAttendance/ tests/ONEVO.Tests.Unit/Features/Calendar/GetEventMeetingAttendanceQueryHandlerTests.cs
git commit -m "feat(calendar): expose meeting create/remove/attendance API endpoints"
```

---

## Task 10: `TeamsAttendanceSyncJob`

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register, next to line 613's `CalendarSyncJob` registration)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventMeetingRepository.GetDueForAttendanceSyncAsync` (Task 3),
  `IExternalCalendarConnectionRepository`, `ICalendarConnectionTokenProvider` (Task 4),
  `ITeamsMeetingClient.GetAttendanceAsync` (Task 5), `ICalendarEventMeetingAttendanceRepository` (Task 3).

- [ ] **Step 1: Write the failing job test**, mirroring `CalendarSyncJobTests.cs`'s exact
  `ServiceCollection`-of-mocks approach (that file registers mocked leaf dependencies as
  singletons so `scope.ServiceProvider.GetRequiredService<T>()` resolves them inside
  `RunOnceAsync`; this test does the same with this job's own dependency set):

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs
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

    private static Tenant MakeTenant() => new()
    {
        Id = TenantId, Name = "Acme", Slug = "acme", Status = TenantStatus.Active
    };

    private static CalendarEventMeeting MakeMeeting() => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = Guid.NewGuid(),
        ExternalCalendarConnectionId = Guid.NewGuid(), Provider = CalendarEventMeetingProviders.MicrosoftTeams,
        ExternalMeetingId = "graph-meeting-1", JoinUrl = "https://teams.microsoft.com/l/meetup-join/abc",
        Status = CalendarEventMeetingStatuses.Active
    };

    private static ExternalCalendarConnection MakeConnection(Guid id) => new()
    {
        Id = id, TenantId = TenantId, UserId = Guid.NewGuid(),
        Provider = CalendarExternalSources.OutlookCalendar, ExternalAccountEmail = "me@acme.com",
        RefreshTokenEncrypted = [1], Status = ExternalCalendarConnectionStatuses.Active
    };

    [Fact]
    public async Task RunOnceAsync_MeetingDueForSync_WritesAttendanceRecordsAndStampsSyncedAt()
    {
        var services = new ServiceCollection();

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        var meeting = MakeMeeting();
        var connection = MakeConnection(meeting.ExternalCalendarConnectionId);

        var meetingsRepoMock = new Mock<ICalendarEventMeetingRepository>();
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { meeting });

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var teamsClientMock = new Mock<ITeamsMeetingClient>();
        teamsClientMock.Setup(t => t.GetAttendanceAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamsAttendanceRecordDto>
            {
                new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow),
                new("Grace Hopper", "grace@acme.com", DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow)
            });

        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(teamsClientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new TeamsAttendanceSyncJob(provider, NullLogger<TeamsAttendanceSyncJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        attendancesRepoMock.Verify(a => a.AddRangeAsync(
            It.Is<IEnumerable<CalendarEventMeetingAttendance>>(records => records.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        meetingsRepoMock.Verify(m => m.Update(
            It.Is<CalendarEventMeeting>(cm => cm.Id == meeting.Id && cm.LastAttendanceSyncedAt != null)), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_OneMeetingThrows_OtherMeetingsInSameTenantStillSync()
    {
        var services = new ServiceCollection();

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock.Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        var failingMeeting = MakeMeeting();
        var succeedingMeeting = MakeMeeting();
        var failingConnection = MakeConnection(failingMeeting.ExternalCalendarConnectionId);
        var succeedingConnection = MakeConnection(succeedingMeeting.ExternalCalendarConnectionId);

        var meetingsRepoMock = new Mock<ICalendarEventMeetingRepository>();
        meetingsRepoMock.Setup(m => m.GetDueForAttendanceSyncAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventMeeting> { failingMeeting, succeedingMeeting });

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, failingConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failingConnection);
        connectionsRepoMock.Setup(c => c.GetByIdForTenantAsync(TenantId, succeedingConnection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(succeedingConnection);

        var tokenProviderMock = new Mock<ICalendarConnectionTokenProvider>();
        tokenProviderMock.Setup(t => t.GetFreshAccessTokenAsync(It.IsAny<ExternalCalendarConnection>(), "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");

        var teamsClientMock = new Mock<ITeamsMeetingClient>();
        teamsClientMock.Setup(t => t.GetAttendanceAsync("access-token", failingMeeting.ExternalMeetingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated Graph 500"));
        teamsClientMock.Setup(t => t.GetAttendanceAsync("access-token", succeedingMeeting.ExternalMeetingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamsAttendanceRecordDto> { new("Ada Lovelace", "ada@acme.com", DateTimeOffset.UtcNow, null) });

        var attendancesRepoMock = new Mock<ICalendarEventMeetingAttendanceRepository>();

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(meetingsRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(tokenProviderMock.Object);
        services.AddSingleton(teamsClientMock.Object);
        services.AddSingleton(attendancesRepoMock.Object);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new TeamsAttendanceSyncJob(provider, NullLogger<TeamsAttendanceSyncJob>.Instance);

        // Should not throw - the failing meeting's exception must be caught and logged, not
        // propagated, and the second meeting must still be synced.
        await job.RunOnceAsync(CancellationToken.None);

        meetingsRepoMock.Verify(m => m.Update(
            It.Is<CalendarEventMeeting>(cm => cm.Id == succeedingMeeting.Id && cm.LastAttendanceSyncedAt != null)), Times.Once);
        meetingsRepoMock.Verify(m => m.Update(
            It.Is<CalendarEventMeeting>(cm => cm.Id == failingMeeting.Id)), Times.Never);
    }
}
```

*(Verify `CalendarExternalSources.OutlookCalendar`'s exact constant name/value against
`ExternalCalendarConnection.cs`'s real source file before writing this step for real — it's
referenced there only in a comment (`"google_calendar" | "outlook_calendar"`), so confirm the
actual constants class name holding those two string values, likely
`CalendarExternalSources` per `CalendarSyncService.cs`'s own
`connection.Provider == CalendarExternalSources.GoogleCalendar` usage seen earlier.)*

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TeamsAttendanceSyncJobTests" --nologo`

- [ ] **Step 3: Write the job**, mirroring `CalendarSyncJob.cs`'s exact tenant-enumeration shape:

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>
/// Polls every active tenant's calendar_event_meetings whose event has ended and whose
/// attendance has never been synced, pulls each one's Teams attendance report, and writes
/// calendar_event_meeting_attendances. Same admin-mode tenant enumeration shape as
/// CalendarSyncJob - see that class's own doc comment for why SetAdminMode() is required first.
/// </summary>
public sealed class TeamsAttendanceSyncJob(IServiceProvider services, ILogger<TeamsAttendanceSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int TenantPageSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "TeamsAttendanceSyncJob run failed."); }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        tenantContext.SetAdminMode();

        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(TenantStatus.Active, searchTerm: null, skip, TenantPageSize, ct);
            if (page.Count == 0) break;

            foreach (var tenant in page)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

                    var meetings = scope.ServiceProvider.GetRequiredService<ICalendarEventMeetingRepository>();
                    var connections = scope.ServiceProvider.GetRequiredService<IExternalCalendarConnectionRepository>();
                    var tokenProvider = scope.ServiceProvider.GetRequiredService<ICalendarConnectionTokenProvider>();
                    var teamsClient = scope.ServiceProvider.GetRequiredService<ITeamsMeetingClient>();
                    var attendances = scope.ServiceProvider.GetRequiredService<ICalendarEventMeetingAttendanceRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    foreach (var meeting in await meetings.GetDueForAttendanceSyncAsync(tenant.Id, ct))
                    {
                        try
                        {
                            await SyncOneMeetingAsync(tenant.Id, meeting, connections, tokenProvider, teamsClient, attendances, meetings, unitOfWork, ct);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Attendance sync failed for meeting {MeetingId} (tenant {TenantId}); skipping.", meeting.Id, tenant.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Attendance sync failed for tenant {TenantId}; skipping.", tenant.Id);
                }
            }

            skip += TenantPageSize;
        }
    }

    private static async Task SyncOneMeetingAsync(
        Guid tenantId, CalendarEventMeeting meeting,
        IExternalCalendarConnectionRepository connections, ICalendarConnectionTokenProvider tokenProvider,
        ITeamsMeetingClient teamsClient, ICalendarEventMeetingAttendanceRepository attendances,
        ICalendarEventMeetingRepository meetings, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return;

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct);
        if (accessToken is null)
            return; // reauth required - retried automatically next run once the connection is fixed

        var records = await teamsClient.GetAttendanceAsync(accessToken, meeting.ExternalMeetingId, ct);
        var toAdd = records.Select(r => new CalendarEventMeetingAttendance
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = meeting.Id,
            ExternalParticipantName = r.ParticipantName, ExternalParticipantEmail = r.ParticipantEmail,
            JoinedAt = r.JoinedAt, LeftAt = r.LeftAt,
            DurationSeconds = r.LeftAt.HasValue ? (int)(r.LeftAt.Value - r.JoinedAt).TotalSeconds : null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await attendances.AddRangeAsync(toAdd, ct);

        meeting.LastAttendanceSyncedAt = DateTimeOffset.UtcNow;
        meetings.Update(meeting);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Register in DI**

In `DependencyInjection.cs`, immediately after line 613's
`services.AddHostedService<Services.Calendar.CalendarSyncJob>();`:

```csharp
        services.AddHostedService<Services.Calendar.TeamsAttendanceSyncJob>();
```

- [ ] **Step 5: Run to verify tests pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TeamsAttendanceSyncJobTests" --nologo`

- [ ] **Step 6: Run the entire backend unit test suite once, as a full-plan checkpoint**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: everything passes — this is the last backend task, so this is the first point a full
regression run makes sense.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs
git commit -m "feat(calendar): add TeamsAttendanceSyncJob"
```

---

## Task 11: Frontend — API service + model additions

**Files:**
- Modify: `src/app/modules/calendar/data-access/calendar-event-api.service.ts` (read it first to
  match its existing HTTP-call style exactly)
- Modify: whatever model file that service already imports its `CalendarEvent`-shaped types from
  (read the service file first to find it)
- Test: the matching `.spec.ts` for the API service (create if none exists, following
  `people-api.service.spec.ts`'s pattern seen earlier this session for HttpClient-mocked service tests)

**Interfaces:**
- Produces: `CalendarEventApiService.createMeeting(eventId)`, `.removeMeeting(eventId)`,
  `.getMeetingAttendance(eventId)` — Task 12/13's components call these exact methods.

- [ ] **Step 1: Read `calendar-event-api.service.ts` in full** to match its base-URL constant,
  `HttpClient` injection style, and return-type conventions exactly before writing anything.

- [ ] **Step 2: Write the failing spec additions** for the three new methods, following whatever
  existing test in that service's `.spec.ts` already asserts a similar POST/DELETE/GET call
  (mirror its `TestBed`/`HttpClientTestingModule` setup verbatim).

- [ ] **Step 3: Run to verify they fail**

Run: `npx ng test --watch=false --include='**/calendar-event-api.service.spec.ts'`

- [ ] **Step 4: Add the three methods**

```typescript
export interface CreateEventMeetingResponse {
  joinUrl: string;
}

export interface MeetingAttendeeResponse {
  name: string | null;
  email: string | null;
  joinedAt: string;
  leftAt: string | null;
  durationSeconds: number | null;
}

export interface MeetingAttendanceResponse {
  available: boolean;
  attendees: MeetingAttendeeResponse[];
}

// Inside the service class, following its existing method style:
createMeeting(eventId: string): Observable<CreateEventMeetingResponse> {
  return this.http.post<CreateEventMeetingResponse>(`${this.baseUrl}/${eventId}/meeting`, {});
}

removeMeeting(eventId: string): Observable<void> {
  return this.http.delete<void>(`${this.baseUrl}/${eventId}/meeting`);
}

getMeetingAttendance(eventId: string): Observable<MeetingAttendanceResponse> {
  return this.http.get<MeetingAttendanceResponse>(`${this.baseUrl}/${eventId}/meeting/attendance`);
}
```

(Adjust `this.baseUrl`/property names to whatever the file's real existing convention is — this
plan's Step 1 exists specifically so this step isn't guessed.)

- [ ] **Step 5: Run to verify they pass**

Run: `npx ng test --watch=false --include='**/calendar-event-api.service.spec.ts'`

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/calendar/data-access/calendar-event-api.service.ts src/app/modules/calendar/data-access/calendar-event-api.service.spec.ts
git commit -m "feat(calendar): add meeting create/remove/attendance API methods"
```

---

## Task 12: Frontend — "Add video call" control in the event form modal

**Files:**
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.ts`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.html`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.spec.ts`
- Modify: `src/app/modules/settings/data-access/calendar-connection-api.service.ts` — no change to
  the file itself, but read it and `calendar-connections.component.ts` first: this task needs to
  know whether the organizer has an active, meeting-scoped Microsoft connection, which is answered
  by calling the *existing* `CalendarConnectionApiService.list()` (already returns
  `CalendarConnection[]` with a `status` field) rather than adding any new endpoint.

**Interfaces:**
- Consumes: `CalendarEventApiService.createMeeting/removeMeeting` (Task 11),
  `CalendarConnectionApiService.list` (existing).

- [ ] **Step 1: Read the current `calendar-event-form-modal.component.ts` in full** (it changed
  significantly earlier this session — do not assume the shape described in any older summary is
  still current) to find: where the `meetingLink` form control lives, where the modal's other
  injected services are listed, and its existing `ngOnChanges`/`onSubmit` structure, since this
  task's new control must sit next to the existing meeting-link input without disturbing the
  meeting-link validation work already shipped.

- [ ] **Step 2: Write the failing component spec additions**

Add cases (mirroring this file's existing `TestBed`/signal-input test style, read from the file
directly before writing these for real):
- "shows 'Add Teams meeting' when an active, meeting-capable Microsoft connection exists and no
  meeting is attached yet"
- "shows a 'Connect Microsoft' prompt linking to Settings when no such connection exists"
- "clicking 'Add Teams meeting' calls createMeeting and populates the read-only join-link display"
- "clicking 'Remove' calls removeMeeting and reverts the field to a free-text input"

- [ ] **Step 3: Run to verify they fail**

Run: `npx ng test --watch=false --include='**/calendar-event-form-modal.component.spec.ts'`

- [ ] **Step 4: Add the component logic**

```typescript
protected readonly hasMeetingCapableConnection = signal(false);
protected readonly attachedMeetingJoinUrl = signal<string | null>(null);
protected readonly meetingActionInFlight = signal(false);

// In ngOnInit (or wherever the modal already loads its own supporting data on open):
this.calendarConnectionApi.list().subscribe((response) => {
  this.hasMeetingCapableConnection.set(
    response.connections.some((c) => c.provider === 'outlook_calendar' && c.status === 'active')
  );
});

protected addTeamsMeeting(): void {
  if (!this.event?.id) return; // meeting can only be added to an already-saved event
  this.meetingActionInFlight.set(true);
  this.calendarEventApi.createMeeting(this.event.id).subscribe({
    next: (response) => {
      this.attachedMeetingJoinUrl.set(response.joinUrl);
      this.form.controls.meetingLink.setValue(response.joinUrl);
      this.meetingActionInFlight.set(false);
    },
    error: () => this.meetingActionInFlight.set(false)
  });
}

protected removeTeamsMeeting(): void {
  if (!this.event?.id) return;
  this.meetingActionInFlight.set(true);
  this.calendarEventApi.removeMeeting(this.event.id).subscribe({
    next: () => {
      this.attachedMeetingJoinUrl.set(null);
      this.form.controls.meetingLink.setValue('');
      this.meetingActionInFlight.set(false);
    },
    error: () => this.meetingActionInFlight.set(false)
  });
}
```

(Field/method names and exact injection points must match whatever Step 1's real read of the file
shows — this is a shape to adapt, not paste verbatim, since the file's actual current signals and
constructor were not re-read as part of writing this plan.)

- [ ] **Step 5: Add the template**

Next to the existing meeting-link input (read the `.html` file's current structure around it
first):

```html
@if (attachedMeetingJoinUrl(); as joinUrl) {
  <div class="flex items-center justify-between rounded-md border px-2.5 py-1.5 text-xs">
    <a [href]="joinUrl" target="_blank" rel="noopener" class="truncate text-[var(--color-accent)]">{{ joinUrl }}</a>
    <button type="button" (click)="removeTeamsMeeting()" [disabled]="meetingActionInFlight()" data-testid="calendar-form-remove-meeting">Remove</button>
  </div>
} @else if (hasMeetingCapableConnection()) {
  <button type="button" (click)="addTeamsMeeting()" [disabled]="meetingActionInFlight()" data-testid="calendar-form-add-teams-meeting">
    + Add Teams meeting
  </button>
} @else {
  <a routerLink="/settings/calendar-connections" class="text-xs text-[var(--color-text-secondary)]" data-testid="calendar-form-connect-microsoft-prompt">
    Connect Microsoft to add a Teams meeting
  </a>
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `npx ng test --watch=false --include='**/calendar-event-form-modal.component.spec.ts'`

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/calendar/ui/calendar-event-form-modal/
git commit -m "feat(calendar): add Teams meeting control to the event form modal"
```

---

## Task 13: Frontend — Attendance section on the event detail view

**Files:**
- Find the existing event-detail component: `grep -rln "calendar-event-detail\|CalendarEventDetail" src/app/modules/calendar/`
- Create: `src/app/modules/calendar/ui/calendar-event-attendance/calendar-event-attendance.component.ts`
- Create: `src/app/modules/calendar/ui/calendar-event-attendance/calendar-event-attendance.component.spec.ts`
- Modify: the event-detail component found above, to render the new component when the event's
  end time has passed.

**Interfaces:**
- Consumes: `CalendarEventApiService.getMeetingAttendance` (Task 11).

- [ ] **Step 1: Read the existing event-detail component** found by the grep above, to match its
  existing standalone-component/signal-input conventions.

- [ ] **Step 2: Write the failing component spec**

```typescript
// calendar-event-attendance.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { CalendarEventApiService } from '../../data-access/calendar-event-api.service';
import { CalendarEventAttendanceComponent } from './calendar-event-attendance.component';

describe('CalendarEventAttendanceComponent', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<CalendarEventAttendanceComponent>>;
  let api: { getMeetingAttendance: ReturnType<typeof vi.fn> };

  function setup(response: { available: boolean; attendees: unknown[] }) {
    api = { getMeetingAttendance: vi.fn().mockReturnValue(of(response)) };
    TestBed.configureTestingModule({
      imports: [CalendarEventAttendanceComponent],
      providers: [{ provide: CalendarEventApiService, useValue: api }]
    }).compileComponents();
    fixture = TestBed.createComponent(CalendarEventAttendanceComponent);
    fixture.componentRef.setInput('eventId', 'event-1');
    fixture.detectChanges();
  }

  it('renders each attendee when data is available', () => {
    setup({
      available: true,
      attendees: [{ name: 'Ada Lovelace', email: 'ada@example.com', joinedAt: '2026-09-23T10:00:00Z', leftAt: '2026-09-23T10:30:00Z', durationSeconds: 1800 }]
    });
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="calendar-attendance-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Ada Lovelace');
  });

  it('shows an unavailable state when attendance has not synced yet', () => {
    setup({ available: false, attendees: [] });
    expect(fixture.nativeElement.querySelector('[data-testid="calendar-attendance-unavailable"]')).toBeTruthy();
  });
});
```

- [ ] **Step 3: Run to verify it fails**

Run: `npx ng test --watch=false --include='**/calendar-event-attendance.component.spec.ts'`

- [ ] **Step 4: Write the component**

```typescript
// calendar-event-attendance.component.ts
import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { CalendarEventApiService, MeetingAttendeeResponse } from '../../data-access/calendar-event-api.service';

@Component({
  selector: 'app-calendar-event-attendance',
  standalone: true,
  imports: [],
  template: `
    @if (available()) {
      <div class="flex flex-col gap-1">
        @for (attendee of attendees(); track attendee.email) {
          <div data-testid="calendar-attendance-row" class="flex items-center justify-between text-xs">
            <span>{{ attendee.name ?? attendee.email ?? 'Unknown participant' }}</span>
            <span class="text-[var(--color-text-secondary)]">{{ formatDuration(attendee.durationSeconds) }}</span>
          </div>
        }
      </div>
    } @else {
      <p data-testid="calendar-attendance-unavailable" class="text-xs text-[var(--color-text-secondary)]">
        Attendance data unavailable
      </p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CalendarEventAttendanceComponent implements OnInit {
  private readonly api = inject(CalendarEventApiService);

  eventId = input.required<string>();
  protected readonly available = signal(false);
  protected readonly attendees = signal<MeetingAttendeeResponse[]>([]);

  ngOnInit(): void {
    this.api.getMeetingAttendance(this.eventId()).subscribe((response) => {
      this.available.set(response.available);
      this.attendees.set(response.attendees);
    });
  }

  protected formatDuration(seconds: number | null): string {
    if (seconds === null) return '';
    const minutes = Math.round(seconds / 60);
    return `${minutes}m`;
  }
}
```

- [ ] **Step 5: Wire it into the event-detail component**

In the event-detail component found in Step 1, add, guarded to only show once the event's end
time has passed (matching whatever `EndDate`-reading pattern that component already uses
elsewhere):

```html
@if (isPastEnd()) {
  <app-calendar-event-attendance [eventId]="event().id" />
}
```

- [ ] **Step 6: Run to verify it passes**

Run: `npx ng test --watch=false --include='**/calendar-event-attendance.component.spec.ts'`

- [ ] **Step 7: Run the whole frontend suite once, as the final plan-wide checkpoint**

Run: `npx ng test --watch=false`
Expected: everything passes (pre-existing flaky specs noted earlier this session —
`wellbeing-carousel-card`, `work.routes`, `notification-bell` — are not this plan's concern if
they fail here; confirm via `git status`/`git log` that this plan's branch didn't touch those
files before assuming a failure there is pre-existing).

- [ ] **Step 8: Commit**

```bash
git add src/app/modules/calendar/ui/calendar-event-attendance/
git add -u src/app/modules/calendar/  # picks up the modified event-detail component
git commit -m "feat(calendar): show Teams meeting attendance on the event detail view"
```

---

## Notes carried from the spec, not resolved by this plan

- **Graph attendance-report admin-consent requirement**: `TeamsAttendanceSyncJob` (Task 10) calls
  `ITeamsMeetingClient.GetAttendanceAsync`, which needs `OnlineMeetingArtifact.Read.All`. Whether
  the organizer's own delegated consent is sufficient, or whether this tenant's Microsoft 365 admin
  must separately grant application-level consent, can only be confirmed against a real tenant
  during manual end-to-end testing after Task 10 ships — if delegated-only access turns out
  insufficient, `GetAttendanceAsync` will surface a 403 from Graph, which the job's existing
  per-meeting `catch` (Task 10, Step 3) already logs and skips without failing the run; the UI's
  "Attendance data unavailable" state (Task 13) already covers this outcome without further code
  changes — nothing to fix ahead of time, just to verify once real data is available.
- **Phase 2 (Zoom)**: not started by this plan. Blocked on a real Zoom Marketplace OAuth app per
  the spec.
