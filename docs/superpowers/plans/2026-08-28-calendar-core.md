# Calendar Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Calendar module's core: `calendar_events` + `calendar_event_participants` tables and create/update/delete/list CRUD, so employees can create manual calendar events and see their own calendar for a date range.

**Architecture:** Standard MediatR CQRS (Domain → Application → Infrastructure → Api), tenant-scoped via `TenantPolicy` + Postgres RLS, matching every other tenant feature in this codebase.

**Tech Stack:** .NET 10, EF Core (PostgreSQL, snake_case convention), MediatR, xUnit + Moq + SQLite in-memory for repository tests.

**Spec:** `docs/superpowers/specs/2026-08-28-calendar-core-external-sync-design.md` (this plan implements the "Calendar Core (CQRS)" section only — external sync is a separate follow-up plan).

## Global Constraints

- Branch off `feature/calendar-oauth-scopes`, not bare `development` (see spec's Global Constraints for why).
- Tenant routes: `api/v1/calendar`, `[Authorize(Policy = "TenantPolicy")]` + per-action `[RequirePermission("calendar:read"|"calendar:write")]`.
- Permissions/module already fully seeded — do not add anything to `PermissionSeeder.cs`/`ModuleCatalogSeeder.cs`/`ModuleAutoGrants.cs`.
- Snake_case DB columns are automatic via the project's `UseSnakeCaseNamingConvention()` — write PascalCase C# properties as normal.
- Every write handler runs inside `IUnitOfWork.ExecuteInTransactionAsync(...)`.
- Result pattern: `Result<T>.Success(value)` / `.Forbidden(msg)` / `.NotFound(msg)` / `.Failure(msg, statusCode)` — never throw for expected failure paths.

---

### Task 1: Domain entities, EF configuration, and migration

**Files:**
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs`
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventParticipant.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventParticipantConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add two `DbSet<T>` properties
- Migration: generated via `dotnet ef migrations add AddCalendarCore`

**Interfaces:**
- Produces: `CalendarEvent` (properties: `Id, TenantId, Title, Description, StartDate, EndDate, SourceType, SourceId, Color, Recurrence, ExternalId, ExternalSource, IsAllDay, Timezone, EventStatus, IsPrivate, OrganizerName, OrganizerEmail, Location, MeetingLink, ExternalAttendeesJson, RecurrenceRule, ExternalUpdatedAt, CreatedById, CreatedAt, UpdatedAt, IsDeleted, DeletedAt`), `CalendarEventParticipant` (`Id, EventId, EmployeeId, ResponseStatus, ResponseReason, CreatedAt, UpdatedAt`), `CalendarEventSourceTypes.{Manual,ExternalSync}`, `CalendarEventParticipantStatuses.{Pending,Accepted,Rejected,ResolutionRequested,ReplacementNominated}`.

- [ ] **Step 1: Write the domain entities**

```csharp
// src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventSourceTypes
{
    public const string Manual = "manual";
    public const string ExternalSync = "external_sync";
    // "holiday" / "schedule_overlay" / "time_off_request" are reserved for later specs -
    // this pass never writes them, but the column must accept them without a future migration.
}

public static class CalendarExternalSources
{
    public const string GoogleCalendar = "google_calendar";
    public const string OutlookCalendar = "outlook_calendar";
}

public static class CalendarRecurrences
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
}

public static class CalendarEventStatuses
{
    public const string Confirmed = "confirmed";
    public const string Tentative = "tentative";
    public const string Cancelled = "cancelled";
}

public class CalendarEvent : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public string SourceType { get; set; } = CalendarEventSourceTypes.Manual;
    public Guid? SourceId { get; set; }
    public string? Color { get; set; }
    public string Recurrence { get; set; } = CalendarRecurrences.None;
    public string? ExternalId { get; set; }
    public string? ExternalSource { get; set; }
    public bool IsAllDay { get; set; }
    public string? Timezone { get; set; }
    public string? EventStatus { get; set; }
    public bool IsPrivate { get; set; }
    public string? OrganizerName { get; set; }
    public string? OrganizerEmail { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }
    public string? ExternalAttendeesJson { get; set; }
    public string? RecurrenceRule { get; set; }
    public DateTimeOffset? ExternalUpdatedAt { get; set; }
    public Guid CreatedById { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventParticipant.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class CalendarEventParticipantStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string ResolutionRequested = "resolution_requested";
    public const string ReplacementNominated = "replacement_nominated";
}

public class CalendarEventParticipant : BaseEntity
{
    public Guid EventId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ResponseStatus { get; set; } = CalendarEventParticipantStatuses.Pending;
    public string? ResponseReason { get; set; }
}
```

Check `src/ONEVO.Domain/Common/BaseEntity.cs` first - if it already carries
`Id`/`TenantId`/`CreatedAt`/`UpdatedAt`/`IsDeleted`/`DeletedAt` (every other entity in this
codebase inherits from it, e.g. `WorkTask : BaseEntity`), these two entities need no explicit
declaration of those fields - only the properties shown above are new. If `BaseEntity` does not
exist under that exact name, grep `WorkTask.cs`'s base class and match it exactly instead.

- [ ] **Step 2: Write the EF configurations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("calendar_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Color).HasMaxLength(7);
        builder.Property(e => e.Recurrence).HasMaxLength(20).IsRequired().HasDefaultValue(CalendarRecurrences.None);
        builder.Property(e => e.ExternalId).HasMaxLength(255);
        builder.Property(e => e.ExternalSource).HasMaxLength(30);
        builder.Property(e => e.Timezone).HasMaxLength(50);
        builder.Property(e => e.EventStatus).HasMaxLength(20);
        builder.Property(e => e.OrganizerName).HasMaxLength(200);
        builder.Property(e => e.OrganizerEmail).HasMaxLength(255);
        builder.Property(e => e.Location).HasMaxLength(500);
        builder.Property(e => e.MeetingLink).HasMaxLength(500);
        builder.Property(e => e.ExternalAttendeesJson).HasColumnName("external_attendees").HasColumnType("jsonb");

        builder.HasIndex(e => new { e.TenantId, e.StartDate, e.EndDate })
            .HasDatabaseName("ix_calendar_events_tenant_id_start_date_end_date");

        builder.HasIndex(e => new { e.TenantId, e.CreatedById })
            .HasDatabaseName("ix_calendar_events_tenant_id_created_by_id");
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventParticipantConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public class CalendarEventParticipantConfiguration : IEntityTypeConfiguration<CalendarEventParticipant>
{
    public void Configure(EntityTypeBuilder<CalendarEventParticipant> builder)
    {
        builder.ToTable("calendar_event_participants");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.ResponseStatus).HasMaxLength(30).IsRequired().HasDefaultValue(CalendarEventParticipantStatuses.Pending);

        builder.HasIndex(p => new { p.TenantId, p.EventId, p.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_participants_one_row_per_employee");

        builder.HasIndex(p => new { p.TenantId, p.EmployeeId })
            .HasDatabaseName("ix_calendar_event_participants_tenant_id_employee_id");
    }
}
```

- [ ] **Step 3: Register the two new `DbSet<T>` properties**

Open `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`, find where `WorkTasks`
(or any other `DbSet<T>`) is declared, and add next to the other feature `DbSet`s:

```csharp
public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
public DbSet<CalendarEventParticipant> CalendarEventParticipants => Set<CalendarEventParticipant>();
```

Add `using ONEVO.Domain.Features.Calendar.Entities;` to the file's using block if not already
covered by a wildcard-style existing import pattern.

- [ ] **Step 4: Build to confirm the entities/configs compile**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

If the backend dev server is currently running, stop it first (it locks the DLLs this build
needs to overwrite) - restart it after Step 6.

- [ ] **Step 5: Generate the migration**

Set the migration connection string (design-time factory doesn't load `.env`):

```bash
export ConnectionStrings__MigrationConnection="Host=localhost;Port=5432;Database=OnevoDb;Username=onevo_migrator;Password=dapiyshanth@19"
```

Run: `dotnet ef migrations add AddCalendarCore --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: a new file `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddCalendarCore.cs` and an
updated `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 6: Add the RLS policy SQL to the generated migration's `Up()`/`Down()`**

Open the generated migration file. After the `CreateTable`/`CreateIndex` calls EF generated,
add this exact block (mirrors `20260823172054_AddTaskCategories.cs`) right before the closing
brace of `Up()`:

```csharp
private static readonly string[] TenantTables = ["calendar_events", "calendar_event_participants"];

// (add this foreach at the end of Up(), after the CreateTable/CreateIndex calls EF generated)
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
```

And this in `Down()`, before the `DropTable` calls:

```csharp
foreach (var table in TenantTables)
{
    migrationBuilder.Sql($@"
        DROP POLICY IF EXISTS tenant_isolation ON {table};
        ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
    ");
}
```

(`TenantTables` is declared once as a private static field on the migration class - place it
right after the class declaration, same as the reference migration.)

- [ ] **Step 7: Apply the migration locally**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: `Applying migration '<timestamp>_AddCalendarCore'.` then `Done.`

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/ src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/ src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(calendar): add calendar_events and calendar_event_participants schema"
```

---

### Task 2: Repository

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventRepositoryTests.cs`

**Interfaces:**
- Consumes: `CalendarEvent`, `CalendarEventParticipant` from Task 1.
- Produces: `ICalendarEventRepository` with `AddAsync`, `GetByIdForTenantAsync`, `GetTrackedByIdForTenantAsync`, `GetInDateRangeForCallerAsync(tenantId, employeeId, userId, from, to, ct)`, `Update`, `Remove`, plus `AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant>, ct)`.

- [ ] **Step 1: Write the repository interface**

```csharp
// src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventRepository
{
    Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<CalendarEvent?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Events in [from, to] where the caller is the creator (by UserId) or a
    /// participant (by EmployeeId) - the two ways of "owning" a calendar event in this pass.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant> participants, CancellationToken ct = default);
    void Update(CalendarEvent calendarEvent);
    void Remove(CalendarEvent calendarEvent);
}
```

- [ ] **Step 2: Write the failing repository tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfCalendarEventRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset RangeStart = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEnd = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetInDateRangeForCallerAsync_ReturnsEventsCreatedByCaller_WithinRange()
    {
        await using var db = BuildInMemoryDb();
        var inRange = MakeEvent(StartDate: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), createdById: UserId);
        var outOfRange = MakeEvent(StartDate: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero), createdById: UserId);
        var otherUsers = MakeEvent(StartDate: new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero), createdById: Guid.NewGuid());
        db.CalendarEvents.AddRange(inRange, outOfRange, otherUsers);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, RangeStart, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(inRange.Id, result[0].Id);
    }

    [Fact]
    public async Task GetInDateRangeForCallerAsync_ReturnsEventsWhereCallerIsParticipant()
    {
        await using var db = BuildInMemoryDb();
        var event1 = MakeEvent(StartDate: new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero), createdById: Guid.NewGuid());
        db.CalendarEvents.Add(event1);
        db.CalendarEventParticipants.Add(new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EventId = event1.Id, EmployeeId = EmployeeId,
            ResponseStatus = CalendarEventParticipantStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetInDateRangeForCallerAsync(TenantId, Guid.NewGuid(), EmployeeId, RangeStart, RangeEnd, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(event1.Id, result[0].Id);
    }

    private static CalendarEvent MakeEvent(DateTimeOffset StartDate, Guid createdById) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, Title = "Event", StartDate = StartDate,
        EndDate = StartDate.AddHours(1), SourceType = CalendarEventSourceTypes.Manual,
        CreatedById = createdById, CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfCalendarEventRepositoryTests" --nologo`
Expected: FAIL - `EfCalendarEventRepository` does not exist yet.

- [ ] **Step 4: Write the repository implementation**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventRepository : ICalendarEventRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
        => await _db.CalendarEvents.AddAsync(calendarEvent, ct);

    public async Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.CalendarEvents.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<CalendarEvent?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.CalendarEvents.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.StartDate <= to && e.EndDate >= from
                        && (e.CreatedById == userId
                            || (employeeId != null && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))))
            .OrderBy(e => e.StartDate)
            .ToListAsync(ct);
    }

    public async Task AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant> participants, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AddRangeAsync(participants, ct);

    public void Update(CalendarEvent calendarEvent) => _db.CalendarEvents.Update(calendarEvent);
    public void Remove(CalendarEvent calendarEvent) => _db.CalendarEvents.Remove(calendarEvent);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfCalendarEventRepositoryTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/ tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventRepositoryTests.cs
git commit -m "feat(calendar): add ICalendarEventRepository and EF implementation"
```

---

### Task 3: GetCalendarEventsQuery

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQuery.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.GetInDateRangeForCallerAsync` (Task 2), `IEmployeeRepository.GetDefaultForUserAsync` (existing, used the same way in `GetMyActivityTimelineQueryHandler`).
- Produces: `GetCalendarEventsQuery(DateTimeOffset From, DateTimeOffset To)`, `CalendarEventItem` record, `CalendarEventsResponse(IReadOnlyList<CalendarEventItem> Events)`.

- [ ] **Step 1: Write the response DTO**

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
    Guid CreatedById);

public sealed record CalendarEventsResponse(IReadOnlyList<CalendarEventItem> Events);
```

- [ ] **Step 2: Write the query and its failing handler test**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed record GetCalendarEventsQuery(DateTimeOffset From, DateTimeOffset To) : IRequest<Result<CalendarEventsResponse>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetCalendarEventsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarEventRepository> _events = new();

    private GetCalendarEventsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, UserId = UserId, TenantId = TenantId });
        return new GetCalendarEventsQueryHandler(_currentUser.Object, _employees.Object, _events.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetCalendarEventsQueryHandler(_currentUser.Object, _employees.Object, _events.Object);

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MapsRepositoryRowsToResponse()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEvent
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, Title = "Sprint Planning",
                    StartDate = From.AddDays(2), EndDate = From.AddDays(2).AddHours(1),
                    SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.None,
                    CreatedById = UserId, CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Events);
        Assert.Equal("Sprint Planning", result.Value.Events[0].Title);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetCalendarEventsQueryHandlerTests" --nologo`
Expected: FAIL - `GetCalendarEventsQueryHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

public sealed class GetCalendarEventsQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ICalendarEventRepository events)
    : IRequestHandler<GetCalendarEventsQuery, Result<CalendarEventsResponse>>
{
    public async Task<Result<CalendarEventsResponse>> Handle(GetCalendarEventsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);

        var rows = await events.GetInDateRangeForCallerAsync(
            tenantId, currentUser.UserId, employee?.Id, request.From, request.To, ct);

        var items = rows.Select(e => new CalendarEventItem(
            e.Id, e.Title, e.Description, e.StartDate, e.EndDate, e.SourceType, e.Color,
            e.Recurrence, e.IsAllDay, e.Timezone, e.EventStatus, e.IsPrivate, e.Location,
            e.MeetingLink, e.ExternalSource, e.CreatedById)).ToList();

        return Result<CalendarEventsResponse>.Success(new CalendarEventsResponse(items));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetCalendarEventsQueryHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/DTOs/ src/ONEVO.Application/Features/Calendar/Queries/ tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs
git commit -m "feat(calendar): add GetCalendarEventsQuery"
```

---

### Task 4: CreateCalendarEventCommand

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CreateCalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.AddAsync`/`AddParticipantsAsync` (Task 2), `IUnitOfWork.ExecuteInTransactionAsync`.
- Produces: `CreateCalendarEventCommand(string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate, bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color, string Recurrence, IReadOnlyList<Guid> ParticipantEmployeeIds)`, returns `Result<CalendarEventItem>` (reuses Task 3's DTO).

- [ ] **Step 1: Write the command and its failing handler test**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;

public sealed record CreateCalendarEventCommand(
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence,
    IReadOnlyList<Guid> ParticipantEmployeeIds) : IRequest<Result<CalendarEventItem>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CreateCalendarEventCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CreateCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarEventItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarEventItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new CreateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new CreateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None, []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CreatesEventAndParticipants()
    {
        var sut = BuildSut();
        var participantId = Guid.NewGuid();

        var result = await sut.Handle(
            new CreateCalendarEventCommand(
                "Sprint Planning", "Plan the sprint", Start, Start.AddHours(1), false, "UTC",
                "Room 4", null, "#2563EB", CalendarRecurrences.None, [participantId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sprint Planning", result.Value!.Title);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.Title == "Sprint Planning" && e.TenantId == TenantId && e.CreatedById == UserId), It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.AddParticipantsAsync(
            It.Is<IReadOnlyList<CalendarEventParticipant>>(p => p.Count == 1 && p[0].EmployeeId == participantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(-1), false, "UTC", null, null, null, CalendarRecurrences.None, []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CreateCalendarEventCommandHandlerTests" --nologo`
Expected: FAIL - `CreateCalendarEventCommandHandler` does not exist yet.

- [ ] **Step 3: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;

public sealed class CreateCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCalendarEventCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(CreateCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<CalendarEventItem>.Failure("Title is required.", 400);

        var tenantId = currentUser.TenantId;

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Title = request.Title.Trim(),
                Description = request.Description, StartDate = request.StartDate, EndDate = request.EndDate,
                SourceType = CalendarEventSourceTypes.Manual, IsAllDay = request.IsAllDay,
                Timezone = request.Timezone, Location = request.Location, MeetingLink = request.MeetingLink,
                Color = request.Color, Recurrence = request.Recurrence,
                CreatedById = currentUser.UserId, CreatedAt = now
            };
            await events.AddAsync(calendarEvent, innerCt);

            if (request.ParticipantEmployeeIds.Count > 0)
            {
                var participants = request.ParticipantEmployeeIds.Select(employeeId => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = calendarEvent.Id, EmployeeId = employeeId,
                    ResponseStatus = CalendarEventParticipantStatuses.Pending, CreatedAt = now
                }).ToList();
                await events.AddParticipantsAsync(participants, innerCt);
            }

            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarEventItem>.Success(new CalendarEventItem(
                calendarEvent.Id, calendarEvent.Title, calendarEvent.Description, calendarEvent.StartDate,
                calendarEvent.EndDate, calendarEvent.SourceType, calendarEvent.Color, calendarEvent.Recurrence,
                calendarEvent.IsAllDay, calendarEvent.Timezone, calendarEvent.EventStatus, calendarEvent.IsPrivate,
                calendarEvent.Location, calendarEvent.MeetingLink, calendarEvent.ExternalSource, calendarEvent.CreatedById));
        }, ct);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CreateCalendarEventCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/ tests/ONEVO.Tests.Unit/Features/Calendar/CreateCalendarEventCommandHandlerTests.cs
git commit -m "feat(calendar): add CreateCalendarEventCommand"
```

---

### Task 5: UpdateCalendarEventCommand

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.GetTrackedByIdForTenantAsync`/`Update` (Task 2).
- Produces: `UpdateCalendarEventCommand(Guid Id, string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate, bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color, string Recurrence)`.

- [ ] **Step 1: Write the command and its failing handler test**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;

public sealed record UpdateCalendarEventCommand(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Timezone,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence) : IRequest<Result<CalendarEventItem>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarEventCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class UpdateCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private UpdateCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarEventItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarEventItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new UpdateCalendarEventCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = Guid.NewGuid(), Title = "Old" });

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesFields()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", "New desc", Start.AddHours(2), Start.AddHours(3), false, "UTC", "Room 1", null, "#ff0000", CalendarRecurrences.Weekly),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", existing.Title);
        Assert.Equal(CalendarRecurrences.Weekly, existing.Recurrence);
        _events.Verify(x => x.Update(existing), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~UpdateCalendarEventCommandHandlerTests" --nologo`
Expected: FAIL - `UpdateCalendarEventCommandHandler` does not exist yet.

- [ ] **Step 3: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;

public sealed class UpdateCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCalendarEventCommand, Result<CalendarEventItem>>
{
    public async Task<Result<CalendarEventItem>> Handle(UpdateCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarEventItem>.Forbidden();

        if (request.EndDate < request.StartDate)
            return Result<CalendarEventItem>.Failure("End date cannot be before start date.", 400);

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<CalendarEventItem>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<CalendarEventItem>.Forbidden("Only the event creator can edit this event.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.Title = request.Title.Trim();
            existing.Description = request.Description;
            existing.StartDate = request.StartDate;
            existing.EndDate = request.EndDate;
            existing.IsAllDay = request.IsAllDay;
            existing.Timezone = request.Timezone;
            existing.Location = request.Location;
            existing.MeetingLink = request.MeetingLink;
            existing.Color = request.Color;
            existing.Recurrence = request.Recurrence;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            events.Update(existing);
            await unitOfWork.SaveChangesAsync(innerCt);

            return Result<CalendarEventItem>.Success(new CalendarEventItem(
                existing.Id, existing.Title, existing.Description, existing.StartDate, existing.EndDate,
                existing.SourceType, existing.Color, existing.Recurrence, existing.IsAllDay, existing.Timezone,
                existing.EventStatus, existing.IsPrivate, existing.Location, existing.MeetingLink,
                existing.ExternalSource, existing.CreatedById));
        }, ct);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~UpdateCalendarEventCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/ tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarEventCommandHandlerTests.cs
git commit -m "feat(calendar): add UpdateCalendarEventCommand"
```

---

### Task 6: DeleteCalendarEventCommand

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/DeleteCalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.GetTrackedByIdForTenantAsync`/`Remove` (Task 2).
- Produces: `DeleteCalendarEventCommand(Guid Id)` → `Result<Unit>` (MediatR's `Unit`, matching `DeleteTaskCommand`'s own return shape - check that file's exact return type before writing this and match it exactly rather than assuming `Unit`).

- [ ] **Step 1: Confirm the existing `DeleteTaskCommand`'s return type**

Read `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTask/DeleteTaskCommand.cs`
and its handler. Use the exact same `Result<T>` return type for `DeleteCalendarEventCommand` (it
is very likely `Result<Unit>` referencing `MediatR.Unit`, but confirm before writing code that
won't compile against a mismatched signature).

- [ ] **Step 2: Write the command and its failing handler test**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;

public sealed record DeleteCalendarEventCommand(Guid Id) : IRequest<Result<Unit>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/DeleteCalendarEventCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class DeleteCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DeleteCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<MediatR.Unit>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<MediatR.Unit>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new DeleteCalendarEventCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = Guid.NewGuid(), Title = "Event" });

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_RemovesEvent()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.Remove(existing), Times.Once);
    }
}
```

(If Step 1 found `DeleteTaskCommand` uses a different return type than `Result<Unit>`, update
both the command's `IRequest<...>` and every `Result<Unit>` reference above to match exactly
before running this test.)

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DeleteCalendarEventCommandHandlerTests" --nologo`
Expected: FAIL - `DeleteCalendarEventCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;

public sealed class DeleteCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCalendarEventCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DeleteCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<Unit>.Forbidden();

        var tenantId = currentUser.TenantId;
        var existing = await events.GetTrackedByIdForTenantAsync(tenantId, request.Id, ct);
        if (existing is null)
            return Result<Unit>.NotFound("Calendar event not found.");

        if (existing.CreatedById != currentUser.UserId)
            return Result<Unit>.Forbidden("Only the event creator can delete this event.");

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            events.Remove(existing);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<Unit>.Success(Unit.Value);
        }, ct);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~DeleteCalendarEventCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/ tests/ONEVO.Tests.Unit/Features/Calendar/DeleteCalendarEventCommandHandlerTests.cs
git commit -m "feat(calendar): add DeleteCalendarEventCommand"
```

---

### Task 7: CalendarController + API contracts

**Files:**
- Create: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Architecture` — run the existing suite to confirm no rule breaks (a
  new `TenantPolicy` controller under `Controllers/Tenant/...` should already satisfy every
  existing architecture rule; this task does not add a new rule)

**Interfaces:**
- Consumes: every command/query from Tasks 3–6.
- Produces: `GET/POST/PUT/DELETE api/v1/calendar` per the spec's endpoint table (Calendar Core rows only).

- [ ] **Step 1: Write the API contracts (request/view-model + mapper)**

```csharp
// src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs
using ONEVO.Application.Features.Calendar.DTOs.Responses;

namespace ONEVO.Api.Contracts.Calendar;

public sealed record CreateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color,
    string Recurrence, IReadOnlyList<Guid> ParticipantEmployeeIds);

public sealed record UpdateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Timezone, string? Location, string? MeetingLink, string? Color,
    string Recurrence);

public sealed record CalendarEventViewModel(
    Guid Id, string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    string SourceType, string? Color, string Recurrence, bool IsAllDay, string? Timezone,
    string? EventStatus, bool IsPrivate, string? Location, string? MeetingLink,
    string? ExternalSource, Guid CreatedById);

public sealed record CalendarEventsViewModel(IReadOnlyList<CalendarEventViewModel> Events);

public static class CalendarEventViewModelMapper
{
    public static CalendarEventViewModel ToViewModel(this CalendarEventItem dto) => new(
        dto.Id, dto.Title, dto.Description, dto.StartDate, dto.EndDate, dto.SourceType, dto.Color,
        dto.Recurrence, dto.IsAllDay, dto.Timezone, dto.EventStatus, dto.IsPrivate, dto.Location,
        dto.MeetingLink, dto.ExternalSource, dto.CreatedById);

    public static CalendarEventsViewModel ToViewModel(this CalendarEventsResponse dto) =>
        new(dto.Events.Select(e => e.ToViewModel()).ToList());
}
```

- [ ] **Step 2: Write the controller**

```csharp
// src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Calendar;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;

namespace ONEVO.Api.Controllers.Tenant.Calendar;

[ApiController]
[Route("api/v1/calendar")]
[Authorize(Policy = "TenantPolicy")]
public class CalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public CalendarController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> GetEvents([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCalendarEventsQuery(from, to), ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> Create([FromBody] CreateCalendarEventRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateCalendarEventCommand(
            request.Title, request.Description, request.StartDate, request.EndDate, request.IsAllDay,
            request.Timezone, request.Location, request.MeetingLink, request.Color, request.Recurrence,
            request.ParticipantEmployeeIds), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarEventRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateCalendarEventCommand(
            id, request.Title, request.Description, request.StartDate, request.EndDate, request.IsAllDay,
            request.Timezone, request.Location, request.MeetingLink, request.Color, request.Recurrence), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteCalendarEventCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 3: Build everything**

Stop the running backend dev server first (it locks the DLLs). Then:

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: all Calendar tests pass; total pass count is the pre-existing count plus the ~13 new
Calendar tests added across Tasks 2–6; zero new failures anywhere else.

- [ ] **Step 5: Run the architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass - `CalendarController` inheriting `ControllerBase` under `Controllers/Tenant/`
with `[Authorize(Policy = "TenantPolicy")]` matches every existing tenant controller's shape, so
no architecture rule should need updating. If one does fail, read what it asserts before
changing anything - it is very likely pointing at a real deviation from convention, not a rule
that needs loosening.

- [ ] **Step 6: Manually verify end-to-end**

Restart the backend dev server. Using the browser or a REST client authenticated as a tenant
user: `POST /api/v1/calendar` with a `CreateCalendarEventRequest` body, confirm `201` with the
created event back; `GET /api/v1/calendar?from=...&to=...` covering that date, confirm the event
appears; `PUT /api/v1/calendar/{id}` to rename it, confirm the change persists on a second GET;
`DELETE /api/v1/calendar/{id}`, confirm a following GET no longer returns it.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Api/Contracts/Calendar/ src/ONEVO.Api/Controllers/Tenant/Calendar/
git commit -m "feat(calendar): add CalendarController wiring create/update/delete/list"
```
