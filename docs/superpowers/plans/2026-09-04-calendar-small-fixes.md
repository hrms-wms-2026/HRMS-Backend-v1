# Calendar Small Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close 4 real gaps in the Calendar module — real country holiday sync, richer conflict-check detail, the two missing RSVP recipient actions, and conflict markers on the calendar grid — without touching the 3 larger deferred sub-projects (Time-Off integration, Google/Outlook OAuth sync, Work Management/Schedule overlays).

**Architecture:** Backend is .NET/Clean Architecture (Domain/Application/Infrastructure/Api) with MediatR CQRS and EF Core/PostgreSQL with row-level tenant isolation. Frontend is Angular with NgRx SignalStore. Each of the 4 features is additive to the existing Calendar feature folder (`ONEVO.*/Features/Calendar/...`, `src/app/modules/calendar/...`) — no existing endpoint is deleted; the one existing endpoint that changes shape (`check-conflicts`) gains new fields on its existing response records rather than being replaced, to avoid breaking the multi-participant conflict-check that event creation already correctly relies on.

**Tech Stack:** .NET 10 / C#, MediatR, EF Core, PostgreSQL, xUnit + Moq; Angular (standalone components, `@ngrx/signals`), Vitest.

**Spec:** [docs/superpowers/specs/2026-09-04-calendar-small-fixes-design.md](../specs/2026-09-04-calendar-small-fixes-design.md) — note: Section 2 of that spec ("full endpoint replace") is superseded by Task 5/6 below, which instead adds `OverlapStart`/`OverlapEnd` to the existing multi-employee `check-conflicts` response after discovering during planning that the real endpoint is multi-participant (used by event creation), not the single-employee shape the original 2nd-brain plan doc assumed.

## Global Constraints

- Backend: use the codebase's existing `Result<T>` / `Result` pattern for all handler return types (not exceptions for expected failures).
- Backend: every new entity/table must get the same RLS (`tenant_isolation` policy) treatment as `20260831041001_AddCalendarCore.cs` — generate migrations with `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`, then hand-edit the generated `Up()`/`Down()` to add the RLS `migrationBuilder.Sql(...)` block, matching that file's pattern exactly.
- Backend: run `dotnet test tests/ONEVO.Tests.Unit` after every task; all tests must stay green.
- Frontend: run `npm test -- --watch=false` after every task; all tests must stay green.
- No task in this plan touches the 3 deferred sub-projects listed in the spec.

---

### Task 1: Holiday Calendar Settings — entity, EF configuration, migration

**Files:**
- Create: `src/ONEVO.Domain/Features/Calendar/Entities/HolidayCalendarSettings.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/HolidayCalendarSettingsConfiguration.cs`
- Create (via `dotnet ef migrations add`): `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddHolidayCalendarSettings.cs` (+ `.Designer.cs`)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/HolidayCalendarSettingsTests.cs`

**Interfaces:**
- Produces: `HolidayCalendarSettings` entity with fields `Id, TenantId, LegalEntityId, DefaultCountryCode, OverrideCountryCode, HolidaySyncEnabled, Provider, LastSyncedYear, LastSyncedAt, UpdatedById, CreatedAt, UpdatedAt` and a computed `EffectiveCountryCode => OverrideCountryCode ?? DefaultCountryCode` property (not a column) that Task 2/3 read.

- [ ] **Step 1: Write the entity**

```csharp
// src/ONEVO.Domain/Features/Calendar/Entities/HolidayCalendarSettings.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Calendar.Entities;

public static class HolidayCalendarProviders
{
    public const string NagerHolidays = "nager_holidays";
}

public class HolidayCalendarSettings : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public string DefaultCountryCode { get; set; } = string.Empty;
    public string? OverrideCountryCode { get; set; }
    public bool HolidaySyncEnabled { get; set; } = true;
    public string Provider { get; set; } = HolidayCalendarProviders.NagerHolidays;
    public int? LastSyncedYear { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public Guid? UpdatedById { get; set; }

    public string EffectiveCountryCode => OverrideCountryCode ?? DefaultCountryCode;
}
```

- [ ] **Step 2: Write a failing test for the computed property**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/HolidayCalendarSettingsTests.cs
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class HolidayCalendarSettingsTests
{
    [Fact]
    public void EffectiveCountryCode_UsesOverride_WhenSet()
    {
        var settings = new HolidayCalendarSettings { DefaultCountryCode = "IN", OverrideCountryCode = "US" };
        Assert.Equal("US", settings.EffectiveCountryCode);
    }

    [Fact]
    public void EffectiveCountryCode_FallsBackToDefault_WhenOverrideIsNull()
    {
        var settings = new HolidayCalendarSettings { DefaultCountryCode = "IN", OverrideCountryCode = null };
        Assert.Equal("IN", settings.EffectiveCountryCode);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~HolidayCalendarSettingsTests"`
Expected: FAIL (entity/property don't exist yet) — if the entity file from Step 1 was already saved, this instead compiles and passes; in that case skip straight to Step 4's confirmation.

- [ ] **Step 4: Confirm it passes**

Run the same command. Expected: PASS (2 tests).

- [ ] **Step 5: Write the EF configuration**, matching the style of `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/PersonalCalendarEventConfiguration.cs` (read that file first for the exact conventions - snake_case column names via `.HasColumnName`, tenant filter, etc.):

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/HolidayCalendarSettingsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Calendar;

public sealed class HolidayCalendarSettingsConfiguration : IEntityTypeConfiguration<HolidayCalendarSettings>
{
    public void Configure(EntityTypeBuilder<HolidayCalendarSettings> builder)
    {
        builder.ToTable("holiday_calendar_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.LegalEntityId).HasColumnName("legal_entity_id");
        builder.Property(x => x.DefaultCountryCode).HasColumnName("default_country_code").HasMaxLength(2).IsRequired();
        builder.Property(x => x.OverrideCountryCode).HasColumnName("override_country_code").HasMaxLength(2);
        builder.Property(x => x.HolidaySyncEnabled).HasColumnName("holiday_sync_enabled");
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30).IsRequired();
        builder.Property(x => x.LastSyncedYear).HasColumnName("last_synced_year");
        builder.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
        builder.Property(x => x.UpdatedById).HasColumnName("updated_by_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(x => x.EffectiveCountryCode);

        builder.HasIndex(x => new { x.TenantId, x.LegalEntityId }).IsUnique()
            .HasDatabaseName("ix_holiday_calendar_settings_one_per_legal_entity");
    }
}
```

- [ ] **Step 6: Register the configuration** — confirm `ApplicationDbContext` auto-discovers configurations via `ApplyConfigurationsFromAssembly` (check `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`); if so, no manual registration needed. If entity configurations are registered explicitly instead, add this one alongside the other Calendar configurations.

- [ ] **Step 7: Generate and edit the migration**

```bash
cd src/ONEVO.Infrastructure
dotnet ef migrations add AddHolidayCalendarSettings --startup-project ../ONEVO.Api
```

Open the generated `Up()`/`Down()` and add the same RLS block `AddCalendarCore` uses (lines 96-118 and 122-131 of `20260831041001_AddCalendarCore.cs`), scoped to just `holiday_calendar_settings`.

- [ ] **Step 8: Apply the migration locally and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: migration applies with no errors; `\d holiday_calendar_settings` in psql shows the table with RLS enabled.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/HolidayCalendarSettings.cs \
        src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/HolidayCalendarSettingsConfiguration.cs \
        src/ONEVO.Infrastructure/Migrations/ \
        tests/ONEVO.Tests.Unit/Features/Calendar/HolidayCalendarSettingsTests.cs
git commit -m "feat(calendar): add HolidayCalendarSettings entity and migration"
```

---

### Task 2: Real Nager Holidays provider

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/Calendar/NagerHolidaysClient.cs`
- Create: `src/ONEVO.Infrastructure/Services/Calendar/NagerHolidaysProvider.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (lines 236-239 — replace the two `NoOp*` registrations)
- Test: `tests/ONEVO.Tests.Unit/Infrastructure/Calendar/NagerHolidaysProviderTests.cs`

**Interfaces:**
- Consumes: `ILeaveHolidayProvider.ListHolidaysAsync(Guid tenantId, Guid? legalEntityId, DateOnly startDate, DateOnly endDate, ct)` and `ILeaveCalendarHolidayProvider.ListHolidaysAsync(Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, DateOnly startDate, DateOnly endDate, ct)` (both interfaces already exist, unchanged).
- Produces: `NagerHolidaysProvider : ILeaveHolidayProvider, ILeaveCalendarHolidayProvider` and `INagerHolidaysClient.GetPublicHolidaysAsync(string countryCode, int year, ct) -> IReadOnlyList<NagerHoliday>` (`NagerHoliday(DateOnly Date, string Name, bool NationalHoliday)`), used by Task 3.

- [ ] **Step 1: Write the failing test for the HTTP client wrapper**

```csharp
// tests/ONEVO.Tests.Unit/Infrastructure/Calendar/NagerHolidaysProviderTests.cs
using System.Net;
using Moq;
using Moq.Protected;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Calendar;

public sealed class NagerHolidaysClientTests
{
    [Fact]
    public async Task GetPublicHolidaysAsync_ParsesOnlyNationalHolidays()
    {
        const string json = """
        [
          {"date":"2026-01-01","localName":"New Year","name":"New Year's Day","countryCode":"AT","nationalHoliday":true},
          {"date":"2026-05-01","localName":"Staatsfeiertag","name":"State Holiday","countryCode":"AT","nationalHoliday":false}
        ]
        """;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://nagerholidays.com/api/v4/") };
        var sut = new NagerHolidaysClient(httpClient);

        var result = await sut.GetPublicHolidaysAsync("AT", 2026, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("New Year's Day", result[0].Name);
        Assert.True(result[0].NationalHoliday);
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_UnknownCountry_ReturnsEmptyNotError()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://nagerholidays.com/api/v4/") };
        var sut = new NagerHolidaysClient(httpClient);

        var result = await sut.GetPublicHolidaysAsync("ZZ", 2026, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_ServerError_Throws()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://nagerholidays.com/api/v4/") };
        var sut = new NagerHolidaysClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetPublicHolidaysAsync("AT", 2026, CancellationToken.None));
    }
}
```

(Requires the `Moq.Protected` NuGet package — check `tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`; if absent, add it: `dotnet add tests/ONEVO.Tests.Unit package Moq.Protected`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~NagerHolidaysClientTests"`
Expected: FAIL (compile error — `NagerHolidaysClient` doesn't exist)

- [ ] **Step 3: Implement the client**

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/NagerHolidaysClient.cs
using System.Net.Http.Json;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed record NagerHoliday(DateOnly Date, string Name, bool NationalHoliday);

public interface INagerHolidaysClient
{
    Task<IReadOnlyList<NagerHoliday>> GetPublicHolidaysAsync(string countryCode, int year, CancellationToken ct = default);
}

public sealed class NagerHolidaysClient(HttpClient http) : INagerHolidaysClient
{
    private sealed record NagerHolidayDto(DateOnly Date, string Name, string CountryCode, bool NationalHoliday);

    public async Task<IReadOnlyList<NagerHoliday>> GetPublicHolidaysAsync(string countryCode, int year, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"Holidays/{countryCode}/{year}", ct);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<NagerHolidayDto>>(cancellationToken: ct) ?? [];
        return dtos.Where(d => d.NationalHoliday).Select(d => new NagerHoliday(d.Date, d.Name, d.NationalHoliday)).ToList();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~NagerHolidaysClientTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Write the failing test for the provider adapter**

```csharp
// append to NagerHolidaysProviderTests.cs
using ONEVO.Domain.Features.Calendar.Entities;

public sealed class NagerHolidaysProviderTests
{
    [Fact]
    public async Task ListHolidaysAsync_LeaveRequestOverload_ReturnsDatesInRange()
    {
        var client = new Mock<INagerHolidaysClient>();
        client.Setup(c => c.GetPublicHolidaysAsync("IN", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NagerHoliday(new DateOnly(2026, 1, 26), "Republic Day", true)]);
        var settings = new Mock<IHolidayCalendarSettingsRepository>();
        settings.Setup(s => s.GetByLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HolidayCalendarSettings { DefaultCountryCode = "IN" });
        var sut = new NagerHolidaysProvider(client.Object, settings.Object);

        var result = await sut.ListHolidaysAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 1, 26), result[0]);
    }

    [Fact]
    public async Task ListHolidaysAsync_NoLegalEntityId_ReturnsEmpty()
    {
        var sut = new NagerHolidaysProvider(Mock.Of<INagerHolidaysClient>(), Mock.Of<IHolidayCalendarSettingsRepository>());

        var result = await sut.ListHolidaysAsync(Guid.NewGuid(), null, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Empty(result);
    }
}
```

Note: this references `IHolidayCalendarSettingsRepository`, which Task 3 defines. If doing this task before Task 3, stub the interface here with just the one method needed (`GetByLegalEntityAsync`); Task 3 will own the full interface and its EF implementation.

- [ ] **Step 6: Run to verify it fails, then implement**

```csharp
// src/ONEVO.Infrastructure/Services/Calendar/NagerHolidaysProvider.cs
using ONEVO.Application.Features.Calendar.RepositoryInterfaces; // IHolidayCalendarSettingsRepository, added in Task 3
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.Services;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class NagerHolidaysProvider(INagerHolidaysClient client, IHolidayCalendarSettingsRepository settingsRepo)
    : ILeaveHolidayProvider, ILeaveCalendarHolidayProvider
{
    public async Task<IReadOnlyList<DateOnly>> ListHolidaysAsync(
        Guid tenantId, Guid? legalEntityId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (legalEntityId is null) return [];
        var settings = await settingsRepo.GetByLegalEntityAsync(tenantId, legalEntityId.Value, ct);
        if (settings is null || !settings.HolidaySyncEnabled) return [];
        return await FetchInRangeAsync(settings.EffectiveCountryCode, startDate, endDate, ct);
    }

    public async Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidaysAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        var result = new List<LeaveCalendarHoliday>();
        foreach (var legalEntityId in legalEntityIds)
        {
            var settings = await settingsRepo.GetByLegalEntityAsync(tenantId, legalEntityId, ct);
            if (settings is null || !settings.HolidaySyncEnabled) continue;
            var dates = await FetchInRangeAsync(settings.EffectiveCountryCode, startDate, endDate, ct);
            // NOTE: check LeaveCalendarHoliday's actual constructor shape before writing this line -
            // it wasn't read during planning; match whatever fields it already requires (likely
            // Date + a name/label + legalEntityId).
            result.AddRange(dates.Select(d => new LeaveCalendarHoliday(d, legalEntityId)));
        }
        return result;
    }

    private async Task<IReadOnlyList<DateOnly>> FetchInRangeAsync(string countryCode, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var years = Enumerable.Range(startDate.Year, endDate.Year - startDate.Year + 1);
        var all = new List<DateOnly>();
        foreach (var year in years)
        {
            var holidays = await client.GetPublicHolidaysAsync(countryCode, year, ct);
            all.AddRange(holidays.Select(h => h.Date).Where(d => d >= startDate && d <= endDate));
        }
        return all;
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~NagerHolidaysProviderTests"` until green. Fix the `LeaveCalendarHoliday` construction line against its real shape (read `src/ONEVO.Application/Features/Leave/Calendar/Services/ILeaveCalendarHolidayProvider.cs` for the record definition first).

- [ ] **Step 7: Register in DI, replacing the no-ops**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, replace lines 236-239:

```csharp
services.AddHttpClient<INagerHolidaysClient, NagerHolidaysClient>(client =>
{
    client.BaseAddress = new Uri("https://nagerholidays.com/api/v4/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
services.AddScoped<ONEVO.Application.Features.Leave.Request.Services.ILeaveHolidayProvider,
    ONEVO.Infrastructure.Services.Calendar.NagerHolidaysProvider>();
services.AddScoped<ONEVO.Application.Features.Leave.Calendar.Services.ILeaveCalendarHolidayProvider,
    ONEVO.Infrastructure.Services.Calendar.NagerHolidaysProvider>();
```

- [ ] **Step 8: Full backend test run**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all green, no regressions in Leave module tests that previously relied on the no-op returning empty (check `tests/ONEVO.Tests.Unit/Features/Leave/` for any test asserting zero holidays that now needs a mock `ILeaveHolidayProvider`/`ILeaveCalendarHolidayProvider` instead of hitting the real DI-registered one — Application-layer unit tests should already mock these interfaces per existing patterns, so this should be a non-issue, but verify).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/Calendar/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Infrastructure/Calendar/
git commit -m "feat(calendar): replace no-op holiday providers with real Nager Holidays integration"
```

---

### Task 3: Holiday sync command, repository, and API endpoints

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IHolidayCalendarSettingsRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfHolidayCalendarSettingsRepository.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/SyncHolidayCalendar/SyncHolidayCalendarCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/SyncHolidayCalendar/SyncHolidayCalendarCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/UpdateHolidayCalendarSettings/UpdateHolidayCalendarSettingsCommand.cs` (+ handler)
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetHolidayCalendarSettings/GetHolidayCalendarSettingsQuery.cs` (+ handler)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs` (add 3 actions)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register `IHolidayCalendarSettingsRepository`)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/SyncHolidayCalendarCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `HolidayCalendarSettings` (Task 1), `INagerHolidaysClient` (Task 2), `ICalendarEventRepository.AddAsync` (existing).
- Produces: `IHolidayCalendarSettingsRepository { GetByLegalEntityAsync, GetTrackedByIdAsync, AddAsync, Update, SaveChangesAsync }` used by Task 2's provider and this task's handlers.

- [ ] **Step 1: Write the repository interface**

```csharp
// src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IHolidayCalendarSettingsRepository.cs
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface IHolidayCalendarSettingsRepository
{
    Task<HolidayCalendarSettings?> GetByLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);
    Task<HolidayCalendarSettings?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task AddAsync(HolidayCalendarSettings settings, CancellationToken ct = default);
    void Update(HolidayCalendarSettings settings);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement it** (follow the pattern of an existing simple Calendar repository, e.g. read `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs` for the DbContext-access convention first):

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfHolidayCalendarSettingsRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public sealed class EfHolidayCalendarSettingsRepository(ApplicationDbContext db) : IHolidayCalendarSettingsRepository
{
    public Task<HolidayCalendarSettings?> GetByLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => db.Set<HolidayCalendarSettings>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId, ct);

    public Task<HolidayCalendarSettings?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.Set<HolidayCalendarSettings>().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task AddAsync(HolidayCalendarSettings settings, CancellationToken ct = default)
        => await db.Set<HolidayCalendarSettings>().AddAsync(settings, ct);

    public void Update(HolidayCalendarSettings settings) => db.Set<HolidayCalendarSettings>().Update(settings);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
```

Register in `DependencyInjection.cs`: `services.AddScoped<IHolidayCalendarSettingsRepository, EfHolidayCalendarSettingsRepository>();`

- [ ] **Step 3: Write the failing test for the sync command handler**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/SyncHolidayCalendarCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.SyncHolidayCalendar;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class SyncHolidayCalendarCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SettingsId = Guid.NewGuid();

    [Fact]
    public async Task Handle_SyncsAndDoesNotDuplicateOnReRun()
    {
        var settings = new HolidayCalendarSettings { Id = SettingsId, TenantId = TenantId, DefaultCountryCode = "IN", HolidaySyncEnabled = true };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        var client = new Mock<INagerHolidaysClient>();
        client.Setup(c => c.GetPublicHolidaysAsync("IN", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NagerHoliday(new DateOnly(2026, 1, 26), "Republic Day", true)]);
        var events = new Mock<ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var sut = new SyncHolidayCalendarCommandHandler(currentUser.Object, settingsRepo.Object, client.Object, events.Object,
            Mock.Of<ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork>());

        var result = await sut.Handle(new SyncHolidayCalendarCommand(SettingsId, 2026), CancellationToken.None);

        Assert.True(result.IsSuccess);
        events.Verify(e => e.AddAsync(
            It.Is<ONEVO.Domain.Features.Calendar.Entities.CalendarEvent>(ev => ev.Title == "Republic Day"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2026, settings.LastSyncedYear);
    }

    [Fact]
    public async Task Handle_SyncDisabled_ReturnsFailureWithoutCallingProvider()
    {
        var settings = new HolidayCalendarSettings { Id = SettingsId, TenantId = TenantId, HolidaySyncEnabled = false };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        var client = new Mock<INagerHolidaysClient>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var sut = new SyncHolidayCalendarCommandHandler(currentUser.Object, settingsRepo.Object, client.Object,
            Mock.Of<ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventRepository>(),
            Mock.Of<ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork>());

        var result = await sut.Handle(new SyncHolidayCalendarCommand(SettingsId, 2026), CancellationToken.None);

        Assert.False(result.IsSuccess);
        client.Verify(c => c.GetPublicHolidaysAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

Note: `ICalendarEventRepository` currently has no delete-by-filter method for idempotent re-sync. Add one in this step:

```csharp
// add to ICalendarEventRepository (src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs)
/// <summary>Removes every 'holiday'-sourced event for the tenant in the given year - used to make
/// holiday re-sync idempotent (delete-then-reinsert) instead of accumulating duplicates.</summary>
Task RemoveHolidayEventsForYearAsync(Guid tenantId, int year, CancellationToken ct = default);
```

Implement it in `EfCalendarEventRepository` with an `ExecuteDeleteAsync` filtered on `SourceType == "holiday" && ExternalSource == "country_holiday" && StartDate.Year == year`.

- [ ] **Step 4: Run to verify it fails, then implement the command + handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/SyncHolidayCalendar/SyncHolidayCalendarCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.SyncHolidayCalendar;

public sealed record SyncHolidayCalendarCommand(Guid SettingsId, int Year) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/SyncHolidayCalendar/SyncHolidayCalendarCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;

namespace ONEVO.Application.Features.Calendar.Commands.SyncHolidayCalendar;

public sealed class SyncHolidayCalendarCommandHandler(
    ICurrentUser currentUser,
    IHolidayCalendarSettingsRepository settingsRepo,
    INagerHolidaysClient client,
    ICalendarEventRepository events,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SyncHolidayCalendarCommand, Result>
{
    public async Task<Result> Handle(SyncHolidayCalendarCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result.Forbidden();
        var tenantId = currentUser.TenantId;

        var settings = await settingsRepo.GetTrackedByIdAsync(tenantId, request.SettingsId, ct);
        if (settings is null) return Result.NotFound("Holiday calendar settings not found.");
        if (!settings.HolidaySyncEnabled) return Result.Failure("Holiday sync is disabled for this legal entity.", 409);

        IReadOnlyList<NagerHoliday> holidays;
        try
        {
            holidays = await client.GetPublicHolidaysAsync(settings.EffectiveCountryCode, request.Year, ct);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure($"Could not reach the holiday provider: {ex.Message}", 502);
        }

        await events.RemoveHolidayEventsForYearAsync(tenantId, request.Year, ct);
        foreach (var holiday in holidays)
        {
            await events.AddAsync(new CalendarEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Title = holiday.Name,
                StartDate = holiday.Date.ToDateTime(TimeOnly.MinValue),
                EndDate = holiday.Date.ToDateTime(TimeOnly.MinValue).AddDays(1),
                IsAllDay = true,
                SourceType = CalendarEventSourceTypes.Holiday,
                ExternalSource = "country_holiday",
                CreatedById = currentUser.UserId
            }, ct);
        }

        settings.LastSyncedYear = request.Year;
        settings.LastSyncedAt = DateTimeOffset.UtcNow;
        settingsRepo.Update(settings);

        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await settingsRepo.SaveChangesAsync(innerCt);
            return Result<bool>.Success(true);
        }, ct);

        return Result.Success();
    }
}
```

Check the exact `CalendarEvent` entity's required fields (read `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs` first — this plan assumes `Id/TenantId/Title/StartDate/EndDate/IsAllDay/SourceType/ExternalSource/CreatedById` based on the migration schema in Task 1's context, but confirm property names/types match exactly, especially whether `StartDate`/`EndDate` are `DateTimeOffset` as the migration says, not `DateOnly`) before compiling.

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SyncHolidayCalendarCommandHandlerTests"` until both tests pass.

- [ ] **Step 5: Write GetHolidayCalendarSettingsQuery and UpdateHolidayCalendarSettingsCommand** (both are simple CRUD-shaped, following `RespondToCalendarEventCommandHandler`'s size/style as a template) — query returns the tenant's settings for the caller's legal entity (or 404 if none seeded yet); command updates `OverrideCountryCode`/`HolidaySyncEnabled` on an existing tracked row. Write a test for each covering the happy path and not-found, same pattern as Step 3.

- [ ] **Step 6: Add the 3 controller actions**

```csharp
// add to CalendarController.cs
[HttpGet("holiday-settings")]
[RequirePermission("calendar:admin")]
public async Task<IActionResult> GetHolidaySettings(CancellationToken ct)
{
    var result = await _mediator.Send(new GetHolidayCalendarSettingsQuery(), ct);
    return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPut("holiday-settings/{id:guid}")]
[RequirePermission("calendar:admin")]
public async Task<IActionResult> UpdateHolidaySettings(Guid id, [FromBody] UpdateHolidayCalendarSettingsRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new UpdateHolidayCalendarSettingsCommand(id, request.OverrideCountryCode, request.HolidaySyncEnabled), ct);
    return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpPost("holiday-settings/{id:guid}/sync")]
[RequirePermission("calendar:admin")]
public async Task<IActionResult> SyncHolidays(Guid id, [FromQuery] int year, CancellationToken ct)
{
    var result = await _mediator.Send(new SyncHolidayCalendarCommand(id, year), ct);
    return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

Add `UpdateHolidayCalendarSettingsRequest(string? OverrideCountryCode, bool HolidaySyncEnabled)` to `ONEVO.Api/Contracts/Calendar/`, following the existing request-record style in that folder. Confirm `"calendar:admin"` exists as a real permission constant (search `PermissionCatalog` / similar); if it doesn't exist yet, add it the same way other module-scoped admin permissions are defined.

- [ ] **Step 7: Full backend test run, then commit**

```bash
dotnet test tests/ONEVO.Tests.Unit
git add src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/IHolidayCalendarSettingsRepository.cs \
        src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfHolidayCalendarSettingsRepository.cs \
        src/ONEVO.Application/Features/Calendar/Commands/SyncHolidayCalendar/ \
        src/ONEVO.Application/Features/Calendar/Commands/UpdateHolidayCalendarSettings/ \
        src/ONEVO.Application/Features/Calendar/Queries/GetHolidayCalendarSettings/ \
        src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs \
        src/ONEVO.Api/Contracts/Calendar/ \
        src/ONEVO.Infrastructure/DependencyInjection.cs \
        tests/ONEVO.Tests.Unit/Features/Calendar/SyncHolidayCalendarCommandHandlerTests.cs
git commit -m "feat(calendar): add holiday-settings API (get/update/sync)"
```

---

### Task 4: Conflict response detail — add OverlapStart/OverlapEnd

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQuery.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs` (extend existing file if present — check first; create if not)

**Interfaces:**
- Produces: `CalendarConflict(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle, DateTimeOffset OverlapStart, DateTimeOffset OverlapEnd)` — 2 new trailing fields, existing 4 unchanged, so this is a backward-compatible record change (any code still using positional construction with only 4 args would break at compile time and must be found via a build error, not left silently wrong — the build itself is the check here).

- [ ] **Step 1: Write/extend the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs
// (check if this file already exists first - if so, add this test case to it rather than creating a duplicate)
[Fact]
public async Task Handle_ComputesOverlapAsIntersectionOfQueryRangeAndEventRange()
{
    var employeeId = Guid.NewGuid();
    var tenantId = Guid.NewGuid();
    var queryStart = new DateTimeOffset(2026, 4, 10, 8, 0, 0, TimeSpan.Zero);
    var queryEnd = new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero);
    // Existing event runs 09:00-11:00 - only overlaps the query range 09:00-10:00
    var existingEvent = new CalendarEvent
    {
        Id = Guid.NewGuid(), Title = "Payroll review",
        StartDate = new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero),
        EndDate = new DateTimeOffset(2026, 4, 10, 11, 0, 0, TimeSpan.Zero)
    };
    var employees = new Mock<IEmployeeRepository>();
    employees.Setup(e => e.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Employee { Id = employeeId, FirstName = "Finance", LastName = "Manager" });
    var events = new Mock<ICalendarEventRepository>();
    events.Setup(e => e.GetInDateRangeForEmployeeAsync(tenantId, employeeId, queryStart, queryEnd, It.IsAny<CancellationToken>()))
        .ReturnsAsync([existingEvent]);
    events.Setup(e => e.GetRecurringMastersForEmployeeAsync(tenantId, employeeId, queryEnd, It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);
    var currentUser = new Mock<ICurrentUser>();
    currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
    currentUser.SetupGet(u => u.TenantId).Returns(tenantId);

    var sut = new CheckCalendarConflictsQueryHandler(currentUser.Object, events.Object, employees.Object, Mock.Of<ICalendarRecurrenceExpander>());

    var result = await sut.Handle(new CheckCalendarConflictsQuery([employeeId], queryStart, queryEnd), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var conflict = Assert.Single(result.Value!.Conflicts);
    Assert.Equal(new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero), conflict.OverlapStart);
    Assert.Equal(new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero), conflict.OverlapEnd);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CheckCalendarConflictsQueryHandlerTests"`
Expected: FAIL (compile error - `OverlapStart`/`OverlapEnd` don't exist on `CalendarConflict` yet)

- [ ] **Step 3: Add the fields and the overlap computation**

```csharp
// CheckCalendarConflictsQuery.cs - update the record
public sealed record CalendarConflict(
    Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle,
    DateTimeOffset OverlapStart, DateTimeOffset OverlapEnd);
```

```csharp
// CheckCalendarConflictsQueryHandler.cs - update both places a CalendarConflict is constructed
var realEvents = await events.GetInDateRangeForEmployeeAsync(tenantId, employeeId, request.StartDate, request.EndDate, ct);
foreach (var e in realEvents)
{
    var overlapStart = e.StartDate > request.StartDate ? e.StartDate : request.StartDate;
    var overlapEnd = e.EndDate < request.EndDate ? e.EndDate : request.EndDate;
    conflicts.Add(new CalendarConflict(employeeId, employeeName, e.Id, e.Title, overlapStart, overlapEnd));
}

// ... and in the recurring-master branch:
if (hasUncancelledOccurrence)
{
    var overlapStart = master.StartDate > request.StartDate ? master.StartDate : request.StartDate;
    var overlapEnd = master.EndDate < request.EndDate ? master.EndDate : request.EndDate;
    conflicts.Add(new CalendarConflict(employeeId, employeeName, master.Id, master.Title, overlapStart, overlapEnd));
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CheckCalendarConflictsQueryHandlerTests"`
Expected: PASS

- [ ] **Step 5: Full backend build + test to catch any other `CalendarConflict` construction site**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit`
Fix any other compile error this surfaces (per the interface note above, a positional 4-arg construction elsewhere would now fail to compile — that's the mechanism catching it).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/ tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs
git commit -m "feat(calendar): add overlap time range to conflict-check response"
```

---

### Task 5: Frontend — conflict banner shows overlap time

**Files:**
- Modify: `src/app/modules/calendar/models/calendar-event.model.ts`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.html`
- Test: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.spec.ts` (extend existing)

**Interfaces:**
- Consumes: Task 4's new `overlapStart`/`overlapEnd` fields on the `check-conflicts` response (camelCase over the wire, matching the existing convention for every other field in this file).

- [ ] **Step 1: Update the model**

```typescript
// calendar-event.model.ts
export interface CalendarConflict {
  employeeId: string;
  employeeName: string;
  conflictingEventId: string;
  conflictingEventTitle: string;
  overlapStart: string;
  overlapEnd: string;
}
```

- [ ] **Step 2: Write the failing test**

Find the existing spec's conflict-rendering test (search `calendar-event-form-modal.component.spec.ts` for `conflict`) and extend the mock conflict object with `overlapStart`/`overlapEnd`, then assert the new text appears:

```typescript
it('shows the overlap time range in the conflict warning', async () => {
  // ... existing setup, but with the conflict object including:
  // overlapStart: '2026-04-10T09:00:00+05:30', overlapEnd: '2026-04-10T09:30:00+05:30'
  // ... trigger the same conflict-rendering path the existing test uses
  expect(fixture.nativeElement.textContent).toContain('9:00'); // exact formatted string depends on step 3
});
```

- [ ] **Step 3: Run to verify it fails, then update the template**

```html
<!-- calendar-event-form-modal.component.html, inside the existing conflict @for block -->
<span>{{ conflict.employeeName }} is busy {{ formatOverlap(conflict.overlapStart, conflict.overlapEnd) }} ({{ conflict.conflictingEventTitle }}).</span>
```

Add a `formatOverlap(start: string, end: string): string` method to the component using the same time-formatting utility the rest of the calendar module already uses (check imports in this component / `calendar-day-view` for the existing time-format helper rather than writing a new one) — e.g. `from 9:00 AM to 9:30 AM`.

- [ ] **Step 4: Run to verify it passes**

Run: `npm test -- --watch=false --include="src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.spec.ts"`

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/calendar/models/calendar-event.model.ts src/app/modules/calendar/ui/calendar-event-form-modal/
git commit -m "feat(calendar): show overlap time range in the conflict warning banner"
```

---

### Task 6: Backend — RSVP resolution-requested and nominate-replacement

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommand.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetEligibleNominees/GetEligibleNomineesQuery.cs` (+ handler)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/` (respond request gains optional fields)
- Test: extend `tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs` (find/create)

**Interfaces:**
- Produces: `RespondToCalendarEventCommand(Guid EventId, string ResponseStatus, string? Reason = null, Guid? NomineeEmployeeId = null)`.
- **Scope decision**: "eligible nominees" = other participants already on the same event (via `ICalendarEventRepository.GetParticipantsForEventsAsync`), not the department-mates fallback the original 2nd-brain plan mentioned — no `IEmployeeRepository` method exists for department-scoped lookup, and adding one is out of scope for this small-fixes sub-project. If department-fallback nominees are needed later, that's a follow-up, not part of this task.

- [ ] **Step 1: Write the failing tests for both new response statuses**

```csharp
// add to RespondToCalendarEventCommandHandlerTests.cs (find the existing file - it must exist since Accepted/Rejected are already tested; if genuinely absent, create it following BreakCommandHandlerTests.cs's Fixture pattern)

[Fact]
public async Task Handle_ResolutionRequested_RequiresReasonAndSetsStatus()
{
    // Arrange: participant exists, no reason provided
    var result = await sut.Handle(new RespondToCalendarEventCommand(eventId, "ResolutionRequested", Reason: null), CancellationToken.None);
    Assert.False(result.IsSuccess);
    Assert.Equal(400, result.StatusCode);

    // Arrange again: with reason
    var resultWithReason = await sut.Handle(new RespondToCalendarEventCommand(eventId, "ResolutionRequested", Reason: "Need to check with my team"), CancellationToken.None);
    Assert.True(resultWithReason.IsSuccess);
    Assert.Equal(CalendarEventParticipantStatuses.ResolutionRequested, participant.ResponseStatus);
    Assert.Equal("Need to check with my team", participant.ResponseReason);
}

[Fact]
public async Task Handle_ReplacementNominated_RequiresNomineeAndValidatesEligibility()
{
    // nominee not a co-participant -> failure
    var invalidResult = await sut.Handle(new RespondToCalendarEventCommand(eventId, "ReplacementNominated", NomineeEmployeeId: someOutsiderId), CancellationToken.None);
    Assert.False(invalidResult.IsSuccess);

    // nominee is a co-participant -> success
    var validResult = await sut.Handle(new RespondToCalendarEventCommand(eventId, "ReplacementNominated", NomineeEmployeeId: coParticipantId), CancellationToken.None);
    Assert.True(validResult.IsSuccess);
    Assert.Equal(CalendarEventParticipantStatuses.ReplacementNominated, participant.ResponseStatus);
}
```

Write these against the real fixture pattern from the existing test file once found/created — the snippets above show intent/assertions, not the full Arrange boilerplate, since that depends on the existing fixture helper this file already has for Accepted/Rejected.

- [ ] **Step 2: Run to verify failure, then implement**

```csharp
// RespondToCalendarEventCommand.cs
public sealed record RespondToCalendarEventCommand(
    Guid EventId, string ResponseStatus, string? Reason = null, Guid? NomineeEmployeeId = null) : IRequest<Result>;
```

```csharp
// RespondToCalendarEventCommandHandler.cs - replace the status-mapping block
var normalizedStatus = request.ResponseStatus switch
{
    "Accepted" => CalendarEventParticipantStatuses.Accepted,
    "Rejected" => CalendarEventParticipantStatuses.Rejected,
    "ResolutionRequested" => CalendarEventParticipantStatuses.ResolutionRequested,
    "ReplacementNominated" => CalendarEventParticipantStatuses.ReplacementNominated,
    _ => null
};
if (normalizedStatus is null)
    return Result.Failure("ResponseStatus must be 'Accepted', 'Rejected', 'ResolutionRequested', or 'ReplacementNominated'.", 400);

if (normalizedStatus == CalendarEventParticipantStatuses.ResolutionRequested && string.IsNullOrWhiteSpace(request.Reason))
    return Result.Failure("A reason is required to request conflict resolution.", 400);

// ... existing employee/participant lookup stays the same ...

if (normalizedStatus == CalendarEventParticipantStatuses.ReplacementNominated)
{
    if (request.NomineeEmployeeId is not { } nomineeId)
        return Result.Failure("A nominee is required to nominate a replacement.", 400);
    var coParticipants = await events.GetParticipantsForEventsAsync(tenantId, [request.EventId], ct);
    var isEligible = coParticipants.TryGetValue(request.EventId, out var list)
        && list.Any(p => p.EmployeeId == nomineeId && p.EmployeeId != employee.Id);
    if (!isEligible)
        return Result.Failure("The nominee must be another participant on this event.", 400);
}

participant.ResponseStatus = normalizedStatus;
participant.ResponseReason = request.Reason;
await unitOfWork.SaveChangesAsync(ct);
// TODO(this task, step 3): create the organizer Inbox item here
return Result.Success();
```

- [ ] **Step 3: Wire the organizer Inbox notification** — find the existing pattern used elsewhere in Calendar for notifying a user (the RSVP invite email outbox handler, `CalendarEventInviteEmailOutboxHandler.cs`, is the nearest precedent — check whether Inbox items in this codebase are outbox-driven the same way, or a direct synchronous write via some `IInboxService`/`INotificationService`; match whichever pattern actually exists rather than inventing a third mechanism). Add a test asserting the Inbox item is created for both new statuses.

- [ ] **Step 4: Add the GetEligibleNominees query** (co-participants minus the responder, minus anyone who has already been nominated/accepted):

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetEligibleNominees/GetEligibleNomineesQuery.cs
public sealed record EligibleNominee(Guid EmployeeId, string EmployeeName);
public sealed record GetEligibleNomineesResponse(IReadOnlyList<EligibleNominee> Nominees);
public sealed record GetEligibleNomineesQuery(Guid EventId) : IRequest<Result<GetEligibleNomineesResponse>>;
```

Handler mirrors `RespondToCalendarEventCommandHandler`'s employee/participant lookup, then returns every OTHER participant on the event as an `EligibleNominee`. Write a test for the empty-list case (sole participant, no eligible nominees) and the non-empty case.

- [ ] **Step 5: Add the 2 controller changes**

```csharp
// Respond action - update to pass the new fields through
public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToCalendarEventRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new RespondToCalendarEventCommand(id, request.ResponseStatus, request.Reason, request.NomineeEmployeeId), ct);
    return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}

[HttpGet("{id:guid}/eligible-nominees")]
[RequirePermission("calendar:read")]
public async Task<IActionResult> GetEligibleNominees(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new GetEligibleNomineesQuery(id), ct);
    return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

Update `RespondToCalendarEventRequest` in `ONEVO.Api/Contracts/Calendar/` to add `string? Reason` and `Guid? NomineeEmployeeId`.

- [ ] **Step 6: Full backend test run, then commit**

```bash
dotnet test tests/ONEVO.Tests.Unit
git add -A -- src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/ \
              src/ONEVO.Application/Features/Calendar/Queries/GetEligibleNominees/ \
              src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs \
              src/ONEVO.Api/Contracts/Calendar/ \
              tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs
git commit -m "feat(calendar): wire up ResolutionRequested and ReplacementNominated RSVP actions"
```

---

### Task 7: Frontend — More menu for Request Resolution / Nominate Replacement

**Files:**
- Modify: `src/app/modules/calendar/models/calendar-event.model.ts`
- Modify: `src/app/modules/calendar/data-access/calendar-event-api.service.ts`
- Modify: `src/app/modules/calendar/state/calendar-event.store.ts`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.ts`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.html`
- Test: extend `calendar-event-form-modal.component.spec.ts`

**Interfaces:**
- Consumes: Task 6's `GET /calendar/{id}/eligible-nominees` and the extended `respond` request shape.

- [ ] **Step 1: Update the model and API service**

```typescript
// calendar-event.model.ts
export type RespondToCalendarEventStatus = 'Accepted' | 'Rejected' | 'ResolutionRequested' | 'ReplacementNominated';

export interface RespondToCalendarEventRequest {
  responseStatus: RespondToCalendarEventStatus;
  reason: string | null;
  nomineeEmployeeId: string | null;
}

export interface EligibleNominee {
  employeeId: string;
  employeeName: string;
}

export interface GetEligibleNomineesResponse {
  nominees: readonly EligibleNominee[];
}
```

```typescript
// calendar-event-api.service.ts
respond(eventId: string, responseStatus: RespondToCalendarEventStatus, reason: string | null = null, nomineeEmployeeId: string | null = null): Observable<void> {
  return this.http.post<void>(`${this.baseUrl}/${eventId}/respond`, { responseStatus, reason, nomineeEmployeeId });
}

getEligibleNominees(eventId: string): Observable<GetEligibleNomineesResponse> {
  return this.http.get<GetEligibleNomineesResponse>(`${this.baseUrl}/${eventId}/eligible-nominees`);
}
```

- [ ] **Step 2: Update the store's `respondToEvent` signature** to accept and forward the new optional params (same try/catch/patchState shape as the existing method):

```typescript
async respondToEvent(
  eventId: string, responseStatus: RespondToCalendarEventStatus, from: string, to: string,
  reason: string | null = null, nomineeEmployeeId: string | null = null
): Promise<boolean> {
  patchState(store, { saving: true, actionError: null });
  try {
    await firstValueFrom(api.respond(eventId, responseStatus, reason, nomineeEmployeeId));
    const response = await firstValueFrom(api.getEvents(from, to));
    patchState(store, { saving: false, events: response.events });
    return true;
  } catch (err) {
    patchState(store, { saving: false, actionError: toSafeErrorMessage(err, 'Failed to respond to this event.') });
    return false;
  }
}
```

- [ ] **Step 3: Write the failing component tests**

```typescript
// add to calendar-event-form-modal.component.spec.ts
it('does not show "Nominate replacement" when there are no eligible nominees', async () => {
  // mock getEligibleNominees to return { nominees: [] }
  // ... open the modal on an event the viewer is a participant on ...
  expect(fixture.nativeElement.querySelector('[data-testid="calendar-form-nominate"]')).toBeNull();
});

it('shows "Nominate replacement" and submits the chosen nominee when eligible nominees exist', async () => {
  // mock getEligibleNominees to return one nominee
  // click [data-testid="calendar-form-more"], then [data-testid="calendar-form-nominate"]
  // select the nominee, confirm
  // expect respondToEvent called with ('ReplacementNominated', ..., nomineeId)
});

it('submits a reason when requesting conflict resolution', async () => {
  // click [data-testid="calendar-form-more"], then [data-testid="calendar-form-request-resolution"]
  // type a reason, confirm
  // expect respondToEvent called with ('ResolutionRequested', ..., reason, null)
});
```

- [ ] **Step 4: Run to verify failure, then implement the component**

Add to the component class: a `moreMenuOpen` signal, `eligibleNominees` signal (loaded via `getEligibleNominees` when the menu opens), `resolutionReasonPromptOpen`/`nomineePickerOpen` signals, and handlers that call `store.respondToEvent(...)` with the extra args. Keep this as plain signals + `@if` blocks matching this component's existing style (no new shared dropdown component - this codebase's modal already uses plain buttons, not a dropdown abstraction) - a "More" button toggles a small inline panel with 1-2 buttons, not a floating menu library.

```html
<!-- replace the existing Accept/Decline button row -->
<div class="flex gap-2">
  <app-button type="button" variant="secondary" data-testid="calendar-form-decline" (click)="onRespond('Rejected')">Decline</app-button>
  <app-button type="button" data-testid="calendar-form-accept" (click)="onRespond('Accepted')">Accept</app-button>
  <app-button type="button" variant="secondary" data-testid="calendar-form-more" (click)="toggleMoreMenu()">More</app-button>
</div>

@if (moreMenuOpen()) {
  <div class="flex flex-col gap-2 rounded-md border border-[var(--color-border)] p-2">
    <button type="button" data-testid="calendar-form-request-resolution" (click)="openResolutionPrompt()">Request conflict resolution</button>
    @if (eligibleNominees().length > 0) {
      <button type="button" data-testid="calendar-form-nominate" (click)="openNomineePicker()">Nominate replacement</button>
    }
  </div>
}
```

(Reason-prompt and nominee-picker UI: simplest correct form is a small inline `<textarea>`/`<select>` + confirm button appearing in place of the More menu when opened, matching this modal's existing plain-HTML-control style - no new shared prompt/picker component needed for 2 fields.)

- [ ] **Step 5: Run to verify it passes**

Run: `npm test -- --watch=false --include="src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.spec.ts"`

- [ ] **Step 6: Full frontend test run, then commit**

```bash
npm test -- --watch=false
git add src/app/modules/calendar/
git commit -m "feat(calendar): add Request Resolution / Nominate Replacement to event RSVP"
```

---

### Task 8: Backend — HasConflict flag on GetCalendarEventsQueryHandler

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs`
- Test: extend `tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs` (find/create)

**Interfaces:**
- Produces: `CalendarEventItem` gains a trailing `bool HasConflict = false` field.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_MarksOverlappingEventsAsHasConflict()
{
    // Two events for the caller/employee in the range that overlap each other in time
    // (reuse this handler's existing fixture setup, add a second overlapping event)
    var result = await sut.Handle(new GetCalendarEventsQuery(from, to), CancellationToken.None);
    Assert.True(result.IsSuccess);
    Assert.All(result.Value!.Events, e => Assert.True(e.HasConflict));
}

[Fact]
public async Task Handle_NonOverlappingEvent_HasConflictFalse()
{
    // single event, no overlaps
    var result = await sut.Handle(new GetCalendarEventsQuery(from, to), CancellationToken.None);
    Assert.False(Assert.Single(result.Value!.Events).HasConflict);
}
```

- [ ] **Step 2: Run to verify failure, then implement**

```csharp
// CalendarEventResponse.cs - add trailing field
public sealed record CalendarEventItem(
    Guid Id, string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    string SourceType, string? Color, string Recurrence, bool IsAllDay, string? Timezone,
    string? EventStatus, bool IsPrivate, string? Location, string? MeetingLink, string? ExternalSource,
    Guid CreatedById, bool IsRecurringOccurrence = false, Guid? RecurrenceMasterId = null,
    DateTimeOffset? OriginalStart = null, IReadOnlyList<CalendarEventParticipantSummary>? Participants = null,
    bool HasConflict = false);
```

```csharp
// GetCalendarEventsQueryHandler.cs - after `items` is fully built (both realRows and recurring-occurrence loops), before the final return:
var withConflictFlags = items.Select(item => item with
{
    HasConflict = items.Any(other => other.Id != item.Id && other.StartDate < item.EndDate && item.StartDate < other.EndDate)
}).ToList();

return Result<CalendarEventsResponse>.Success(new CalendarEventsResponse(withConflictFlags.OrderBy(i => i.StartDate).ToList()));
```

This flags any two events in the SAME response (i.e. same caller's visible range) that overlap in time, regardless of employee — matching the grid's purpose (a heads-up on the viewer's own calendar), which is deliberately simpler than the full multi-participant conflict check in Task 4/6 (that one is for evaluating a NEW event being created; this one is "does anything on my visible calendar overlap with anything else on it").

- [ ] **Step 3: Run to verify it passes, then full test run + commit**

```bash
dotnet test tests/ONEVO.Tests.Unit
git add src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs \
        src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/ \
        tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs
git commit -m "feat(calendar): flag overlapping events with HasConflict in the events response"
```

---

### Task 9: Frontend — conflict markers on the grid

**Files:**
- Modify: `src/app/modules/calendar/models/calendar-event.model.ts`
- Modify: `src/app/modules/calendar/ui/calendar-month-grid/calendar-month-grid.component.html`
- Modify: `src/app/modules/calendar/ui/calendar-week-view/calendar-week-view.component.html`
- Modify: `src/app/modules/calendar/ui/calendar-day-view/calendar-day-view.component.html`
- Modify: `src/app/modules/calendar/ui/calendar-agenda-view/calendar-agenda-view.component.html`
- Test: extend each grid component's `.spec.ts`

**Interfaces:**
- Consumes: Task 8's `hasConflict` field on `CalendarEventResponse` (camelCase over the wire).

- [ ] **Step 1: Add the field to the model**

```typescript
// calendar-event.model.ts
export interface CalendarEventResponse {
  // ... existing fields ...
  hasConflict: boolean;
}
```

- [ ] **Step 2: Write the failing test for calendar-month-grid** (the other 3 views follow the identical pattern once this one is proven out)

```typescript
// calendar-month-grid.component.spec.ts
it('renders a conflict indicator on an event chip flagged hasConflict', async () => {
  // set up cells() input with one event where hasConflict: true
  const chip = fixture.nativeElement.querySelector('[data-testid="calendar-event-chip"]');
  expect(chip.querySelector('[data-testid="calendar-conflict-marker"]')).not.toBeNull();
});

it('does not render a conflict indicator on a non-conflicting event chip', async () => {
  // hasConflict: false
  const chip = fixture.nativeElement.querySelector('[data-testid="calendar-event-chip"]');
  expect(chip.querySelector('[data-testid="calendar-conflict-marker"]')).toBeNull();
});
```

- [ ] **Step 3: Run to verify failure, then update the template**

```html
<!-- calendar-month-grid.component.html -->
<button
  type="button"
  data-testid="calendar-event-chip"
  class="flex items-center gap-1 truncate rounded px-1.5 py-0.5 text-left text-[11px] text-white"
  [style.background-color]="event.color || 'var(--color-accent)'"
  (click)="$event.stopPropagation(); eventClick.emit(event.id)"
>
  @if (event.hasConflict) {
    <span data-testid="calendar-conflict-marker" class="h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--color-danger)]" aria-hidden="true"></span>
  }
  <span class="truncate">{{ event.title }}</span>
</button>
```

- [ ] **Step 4: Run to verify it passes**

Run: `npm test -- --watch=false --include="src/app/modules/calendar/ui/calendar-month-grid/calendar-month-grid.component.spec.ts"`

- [ ] **Step 5: Repeat steps 2-4 for week-view, day-view, and agenda-view** — read each component's existing event-chip template first (they won't be byte-identical to month-grid's) and apply the same "conflict-marker dot before the title, gated on `event.hasConflict`" pattern in whatever markup structure each already uses.

- [ ] **Step 6: Full frontend test run, then commit**

```bash
npm test -- --watch=false
git add src/app/modules/calendar/
git commit -m "feat(calendar): show a conflict marker on event chips in all grid views"
```

---

### Task 10: End-to-end verification

- [ ] **Step 1: Full backend suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all green.

- [ ] **Step 2: Full frontend suite**

Run: `npm test -- --watch=false`
Expected: all green.

- [ ] **Step 3: Manual smoke test in the browser** (start both dev servers)

1. As an admin, hit `GET /api/v1/calendar/holiday-settings` — confirm a settings row exists or create one, then `POST .../sync?year=2026` and confirm holiday events appear on the Calendar month view for the legal entity's country.
2. Create an event with 2+ participants where one already has a conflicting event — confirm the conflict banner shows a specific time range, not just "busy".
3. As an invited participant, open an event, click More → Request conflict resolution with a reason — confirm it submits without error and the organizer's Inbox gets an item (if Inbox UI is reachable in this environment).
4. As an invited participant on an event with a co-participant, click More → Nominate replacement — confirm the picker shows only co-participants.
5. As an invited participant on an event with NO co-participants, confirm "Nominate replacement" does not appear at all.
6. View the month/week/day/agenda calendar with two events that overlap in time — confirm both show the small conflict-marker dot; confirm a non-overlapping event does not.

- [ ] **Step 4: Follow the finishing-a-development-branch skill** to decide how to integrate this branch (merge locally / push PR / keep as-is).
