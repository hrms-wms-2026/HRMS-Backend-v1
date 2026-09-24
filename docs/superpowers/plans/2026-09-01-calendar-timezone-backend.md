# Calendar Timezone Correctness — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the frontend a way to resolve "my effective timezone", and make every Calendar event's `Timezone` field mean something real: set once from the organizer's effective timezone at creation, never mutated by any later edit or occurrence-split path.

**Architecture:** A new `ICalendarTimezoneResolver` service centralizes the `Employee.DisplayTimezone ?? LegalEntity.Timezone ?? "UTC"` resolution (the same order `GetMyProfileQueryHandler` already uses), consumed by both a new lightweight query/endpoint and the three existing Calendar write handlers. No storage-shape change - `CalendarEvent.StartDate`/`EndDate` stay UTC `DateTimeOffset` exactly as today.

**Tech Stack:** .NET 10, MediatR, EF Core.

**Spec:** `docs/superpowers/specs/2026-09-01-calendar-timezone-and-unsaved-guard-design.md`. This plan implements the backend half (Parts 1-2). The frontend half (Parts 3-7) is a separate plan in the frontend repo, written after this one ships.

## Global Constraints

- Branch off `feature/calendar-recurrence-engine` (the latest shipped backend Calendar work) in this repo.
- Timezone resolution order is always `Employee.DisplayTimezone ?? LegalEntity.Timezone ?? "UTC"` - the exact order `GetMyProfileQueryHandler` (`src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs:73-78`) already uses. Do not invent a different fallback order.
- Use `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository.GetDefaultForUserAsync` for "resolve the caller's employee" - the convention already established throughout the rest of Calendar's backend (not `Common.RepositoryInterfaces.IEmployeeRepository.GetByUserIdAsync`, which is what `GetMyProfileQueryHandler` happens to use but which is a different-though-similarly-named interface - fully qualify to disambiguate, exactly as every other Calendar handler in this codebase already does).
- `ILegalEntityRepository.GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct)` (`ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs:31`) is the real, confirmed method for resolving a legal entity by id.
- An event's `Timezone` is written exactly once, at creation, from the organizer's resolved effective timezone. Every other write path leaves the existing value alone, including when constructing a new detached child or a new split-off master (those inherit the **original master's** `Timezone`, never a request value).
- `Timezone` is removed from `CreateCalendarEventCommand`/`Request`, `UpdateCalendarEventCommand`/`Request`, and `EditRecurringOccurrenceCommand`/`Request` - the client no longer supplies this value at all. `CalendarEventViewModel.Timezone` (the read-side view model) is untouched - it keeps reporting the organizer's anchor timezone.
- Out of scope, explicitly deferred: DST-safe recurrence expansion (`IcalNetRecurrenceExpander` keeps doing pure-UTC math) - do not touch that file in this plan.

---

### Task 1: `ICalendarTimezoneResolver`, `GetMyEffectiveTimezoneQuery`, and the `/my-timezone` endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Services/ICalendarTimezoneResolver.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Services/CalendarTimezoneResolver.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetMyEffectiveTimezone/GetMyEffectiveTimezoneQuery.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetMyEffectiveTimezone/GetMyEffectiveTimezoneQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarTimezoneResolverTests.cs`, `tests/ONEVO.Tests.Unit/Features/Calendar/GetMyEffectiveTimezoneQueryHandlerTests.cs`

**Interfaces:**
- Produces: `ICalendarTimezoneResolver.ResolveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) : Task<string>`, `GetMyEffectiveTimezoneQuery : IRequest<Result<MyEffectiveTimezoneResponse>>`, `MyEffectiveTimezoneResponse(string Timezone)`.

- [ ] **Step 1: Write the interface**

```csharp
// src/ONEVO.Application/Features/Calendar/Services/ICalendarTimezoneResolver.cs
namespace ONEVO.Application.Features.Calendar.Services;

public interface ICalendarTimezoneResolver
{
    /// <summary>Resolves the effective IANA timezone for one user: their own DisplayTimezone if
    /// set, else their legal entity's Timezone, else "UTC" - the same order GetMyProfileQueryHandler
    /// already uses. Returns "UTC" if the user has no employee record at all.</summary>
    Task<string> ResolveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CalendarTimezoneResolverTests.cs
using Moq;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarTimezoneResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();

    private CalendarTimezoneResolver BuildSut() => new(_employees.Object, _legalEntities.Object);

    [Fact]
    public async Task ResolveForUserAsync_EmployeeHasDisplayTimezone_ReturnsIt()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, DisplayTimezone = "Asia/Colombo" });

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("Asia/Colombo", result);
        _legalEntities.Verify(x => x.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForUserAsync_NoDisplayTimezone_FallsBackToLegalEntity()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, DisplayTimezone = null });
        _legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "America/New_York" });

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("America/New_York", result);
    }

    [Fact]
    public async Task ResolveForUserAsync_NoLegalEntity_FallsBackToUtc()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, LegalEntityId = null, DisplayTimezone = null });

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("UTC", result);
    }

    [Fact]
    public async Task ResolveForUserAsync_NoEmployeeRecord_FallsBackToUtc()
    {
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await BuildSut().ResolveForUserAsync(TenantId, UserId, CancellationToken.None);

        Assert.Equal("UTC", result);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarTimezoneResolverTests" --nologo`
Expected: FAIL - `CalendarTimezoneResolver` does not exist yet.

- [ ] **Step 4: Write the implementation**

```csharp
// src/ONEVO.Application/Features/Calendar/Services/CalendarTimezoneResolver.cs
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarTimezoneResolver(IEmployeeRepository employees, ILegalEntityRepository legalEntities) : ICalendarTimezoneResolver
{
    public async Task<string> ResolveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var employee = await employees.GetDefaultForUserAsync(tenantId, userId, ct);
        if (employee is null) return "UTC";

        if (!string.IsNullOrWhiteSpace(employee.DisplayTimezone))
            return employee.DisplayTimezone;

        if (employee.LegalEntityId is Guid legalEntityId)
        {
            var legalEntity = await legalEntities.GetByIdForTenantAsync(tenantId, legalEntityId, ct);
            if (!string.IsNullOrWhiteSpace(legalEntity?.Timezone))
                return legalEntity!.Timezone!;
        }

        return "UTC";
    }
}
```

Note: this file's `using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;` brings in the correctly-scoped `IEmployeeRepository` with no ambiguity here, since this file does **not** also `using ONEVO.Application.Common.RepositoryInterfaces;` (unlike the command handlers in Task 2, which already have that using for `IUnitOfWork` and must fully-qualify instead - see Task 2's constraint).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarTimezoneResolverTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Write the failing query-handler tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/GetMyEffectiveTimezoneQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;
using ONEVO.Application.Features.Calendar.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetMyEffectiveTimezoneQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarTimezoneResolver> _resolver = new();

    private GetMyEffectiveTimezoneQueryHandler BuildSut() => new(_currentUser.Object, _resolver.Object);

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var result = await BuildSut().Handle(new GetMyEffectiveTimezoneQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Authenticated_ReturnsResolvedTimezone()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _resolver.Setup(x => x.ResolveForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Colombo");

        var result = await BuildSut().Handle(new GetMyEffectiveTimezoneQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asia/Colombo", result.Value!.Timezone);
    }
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyEffectiveTimezoneQueryHandlerTests" --nologo`
Expected: FAIL - the query/handler don't exist yet.

- [ ] **Step 8: Write the query and handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetMyEffectiveTimezone/GetMyEffectiveTimezoneQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;

public sealed record MyEffectiveTimezoneResponse(string Timezone);

public sealed record GetMyEffectiveTimezoneQuery : IRequest<Result<MyEffectiveTimezoneResponse>>;
```

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/GetMyEffectiveTimezone/GetMyEffectiveTimezoneQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;

public sealed class GetMyEffectiveTimezoneQueryHandler(ICurrentUser currentUser, ICalendarTimezoneResolver resolver)
    : IRequestHandler<GetMyEffectiveTimezoneQuery, Result<MyEffectiveTimezoneResponse>>
{
    public async Task<Result<MyEffectiveTimezoneResponse>> Handle(GetMyEffectiveTimezoneQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<MyEffectiveTimezoneResponse>.Forbidden();

        var timezone = await resolver.ResolveForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        return Result<MyEffectiveTimezoneResponse>.Success(new MyEffectiveTimezoneResponse(timezone));
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetMyEffectiveTimezoneQueryHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 10: Add the endpoint**

Add to `CalendarContracts.cs` (near the other view models):
```csharp
public sealed record MyEffectiveTimezoneViewModel(string Timezone);
```
Add to `CalendarController.cs` (with `using ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;`):
```csharp
    [HttpGet("my-timezone")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> GetMyTimezone(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyEffectiveTimezoneQuery(), ct);
        return result.IsSuccess
            ? Ok(new MyEffectiveTimezoneViewModel(result.Value!.Timezone))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 11: Register the resolver in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, alongside the other Calendar service registrations (`ICalendarNotificationSender`, `ICalendarRecurrenceExpander`):
```csharp
        services.AddScoped<ICalendarTimezoneResolver, CalendarTimezoneResolver>();
```
(add `using ONEVO.Application.Features.Calendar.Services;` if not already present - it already is, from the earlier `ICalendarNotificationSender` registration).

- [ ] **Step 12: Build to confirm everything compiles**

Stop the running backend dev server first. Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 13: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Services/ICalendarTimezoneResolver.cs src/ONEVO.Application/Features/Calendar/Services/CalendarTimezoneResolver.cs src/ONEVO.Application/Features/Calendar/Queries/GetMyEffectiveTimezone/ src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarTimezoneResolverTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/GetMyEffectiveTimezoneQueryHandlerTests.cs
git commit -m "feat(calendar): add ICalendarTimezoneResolver and GET /calendar/my-timezone"
```

---

### Task 2: Anchor `Timezone` to the organizer at creation; freeze it on every edit path

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommand.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommand.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommand.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/CreateCalendarEventCommandHandlerTests.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarEventCommandHandlerTests.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/EditRecurringOccurrenceCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarTimezoneResolver.ResolveForUserAsync` (Task 1).

- [ ] **Step 1: Remove `Timezone` from the three command records**

In `CreateCalendarEventCommand.cs`, remove the `string? Timezone,` line (between `IsAllDay` and `Location`), so the record reads:
```csharp
public sealed record CreateCalendarEventCommand(
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence,
    IReadOnlyList<Guid> ParticipantEmployeeIds,
    string? RecurrenceRule = null) : IRequest<Result<CalendarEventItem>>;
```

In `UpdateCalendarEventCommand.cs`, remove the `string? Timezone,` line the same way:
```csharp
public sealed record UpdateCalendarEventCommand(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Location,
    string? MeetingLink,
    string? Color,
    string Recurrence) : IRequest<Result<CalendarEventItem>>;
```

In `EditRecurringOccurrenceCommand.cs`, remove the `string? Timezone,` line the same way:
```csharp
public sealed record EditRecurringOccurrenceCommand(
    Guid MasterId,
    DateTimeOffset OriginalStart,
    RecurrenceEditScope Scope,
    string Title,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsAllDay,
    string? Location,
    string? MeetingLink,
    string? Color) : IRequest<Result<CalendarEventItem>>;
```

- [ ] **Step 2: Update `CreateCalendarEventCommandHandler` to resolve and set `Timezone` itself**

Add `ICalendarTimezoneResolver timezoneResolver` to the constructor (alongside the existing `employees`/`notifications`):
```csharp
public sealed class CreateCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    ICalendarTimezoneResolver timezoneResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCalendarEventCommand, Result<CalendarEventItem>>
```
Add `using ONEVO.Application.Features.Calendar.Services;` (it may already be present from `ICalendarNotificationSender` - check before adding a duplicate).

Replace:
```csharp
                Timezone = request.Timezone, Location = request.Location, MeetingLink = request.MeetingLink,
```
with (resolve inside the transaction, before constructing the entity, so the resolved value is available for `Timezone =` below):
```csharp
                Location = request.Location, MeetingLink = request.MeetingLink,
```
and immediately above the `var calendarEvent = new CalendarEvent` line, insert:
```csharp
            var organizerTimezone = await timezoneResolver.ResolveForUserAsync(tenantId, currentUser.UserId, innerCt);
```
then add `Timezone = organizerTimezone,` into the `CalendarEvent` object initializer (anywhere in the initializer list - e.g. right after `SourceType = CalendarEventSourceTypes.Manual,`).

Update the trailing `return Result<CalendarEventItem>.Success(new CalendarEventItem(...))` construction - it currently reads `calendarEvent.Title, ...` positionally and already includes `calendarEvent.Timezone`... wait, `CalendarEventItem` doesn't take a raw "Timezone" positional slot from this handler today (check: it maps `e.Recurrence, e.IsAllDay, e.Timezone, e.EventStatus, ...` inside `GetCalendarEventsQueryHandler`, but `CreateCalendarEventCommandHandler`'s own return maps `calendarEvent.Recurrence, calendarEvent.IsAllDay, calendarEvent.Timezone, calendarEvent.EventStatus, ...`) - no change needed here, `calendarEvent.Timezone` already flows through correctly once the entity's `Timezone` property itself holds the resolved value.

- [ ] **Step 3: Update `UpdateCalendarEventCommandHandler` to leave `Timezone` untouched**

Delete this line entirely:
```csharp
            existing.Timezone = request.Timezone;
```
No constructor changes needed - this handler does not need `ICalendarTimezoneResolver` (it never sets `Timezone`).

- [ ] **Step 4: Update `EditRecurringOccurrenceCommandHandler` to leave/inherit `Timezone` correctly**

In `ApplyFields`, delete this line entirely:
```csharp
        target.Timezone = request.Timezone;
```
(after this, `EditAllEventsAsync` leaves the master's existing `Timezone` untouched automatically, since `ApplyFields` no longer overwrites it.)

In `EditThisEventOnlyAsync`, inside the `child is null` branch, add `Timezone = master.Timezone,` to the new `CalendarEvent` object initializer:
```csharp
            child = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                RecurrenceOriginalStart = request.OriginalStart, Recurrence = CalendarRecurrences.None,
                SourceType = master.SourceType, Timezone = master.Timezone
            };
```

In `EditThisAndFollowingAsync`, add `Timezone = master.Timezone,` to the new `newMaster` object initializer:
```csharp
            var newMaster = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = null,
                Recurrence = master.Recurrence, RecurrenceRule = originalRule, SourceType = master.SourceType,
                Timezone = master.Timezone
            };
```

- [ ] **Step 5: Update the API contracts**

In `CalendarContracts.cs`, remove `string? Timezone,` from `CreateCalendarEventRequest`, `UpdateCalendarEventRequest`, and `EditRecurringOccurrenceRequest`:
```csharp
public sealed record CreateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Location, string? MeetingLink, string? Color,
    string Recurrence, IReadOnlyList<Guid> ParticipantEmployeeIds, string? RecurrenceRule = null);

public sealed record UpdateCalendarEventRequest(
    string Title, string? Description, DateTimeOffset StartDate, DateTimeOffset EndDate,
    bool IsAllDay, string? Location, string? MeetingLink, string? Color,
    string Recurrence);
```
```csharp
public sealed record EditRecurringOccurrenceRequest(
    DateTimeOffset OriginalStart, string Scope, string Title, string? Description,
    DateTimeOffset StartDate, DateTimeOffset EndDate, bool IsAllDay,
    string? Location, string? MeetingLink, string? Color);
```
Leave `CalendarEventViewModel.Timezone` exactly as-is (read-side, unaffected).

- [ ] **Step 6: Update the controller call sites**

In `CalendarController.cs`, `Create`:
```csharp
        var result = await _mediator.Send(new CreateCalendarEventCommand(
            request.Title, request.Description, request.StartDate, request.EndDate, request.IsAllDay,
            request.Location, request.MeetingLink, request.Color, request.Recurrence,
            request.ParticipantEmployeeIds, request.RecurrenceRule), ct);
```
`Update`:
```csharp
        var result = await _mediator.Send(new UpdateCalendarEventCommand(
            id, request.Title, request.Description, request.StartDate, request.EndDate, request.IsAllDay,
            request.Location, request.MeetingLink, request.Color, request.Recurrence), ct);
```
`EditOccurrence`:
```csharp
        var result = await _mediator.Send(new EditRecurringOccurrenceCommand(
            id, request.OriginalStart, scope, request.Title, request.Description, request.StartDate,
            request.EndDate, request.IsAllDay, request.Location, request.MeetingLink, request.Color), ct);
```

- [ ] **Step 7: Fix the existing test call sites (compile errors otherwise - positional records shift)**

In `CreateCalendarEventCommandHandlerTests.cs`, replace each of the following lines (removing the `"UTC",` positional argument - every other argument keeps its value, just shifts left one slot):
```csharp
new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None, []),
```
→
```csharp
new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None, []),
```
(this exact line appears twice - once as the `Handle_Unauthenticated_ReturnsForbidden` command, once as `Handle_EndDateBeforeStartDate_ReturnsFailure`'s sibling with `.AddHours(-1)`; apply the same `"UTC",` removal to both):
```csharp
new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(-1), false, null, null, null, CalendarRecurrences.None, []),
```
```csharp
new CreateCalendarEventCommand("Solo block", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None, []),
```
```csharp
new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.Weekly, []),
```
The multi-line `Handle_CreatesEventAndParticipants` call:
```csharp
        var result = await sut.Handle(
            new CreateCalendarEventCommand(
                "Sprint Planning", "Plan the sprint", Start, Start.AddHours(1), false, "UTC",
                "Room 4", null, "#2563EB", CalendarRecurrences.None, [participantId]),
            CancellationToken.None);
```
→
```csharp
        var result = await sut.Handle(
            new CreateCalendarEventCommand(
                "Sprint Planning", "Plan the sprint", Start, Start.AddHours(1), false,
                "Room 4", null, "#2563EB", CalendarRecurrences.None, [participantId]),
            CancellationToken.None);
```
And the `Handle_RecurringWithRecurrenceRule_SetsItOnTheEntity` call:
```csharp
            new CreateCalendarEventCommand("Standup", null, Start, Start.AddMinutes(30), false, "UTC", null, null, null,
                CalendarRecurrences.Weekly, [], RecurrenceRule: "FREQ=WEEKLY"),
```
→
```csharp
            new CreateCalendarEventCommand("Standup", null, Start, Start.AddMinutes(30), false, null, null, null,
                CalendarRecurrences.Weekly, [], RecurrenceRule: "FREQ=WEEKLY"),
```

In `UpdateCalendarEventCommandHandlerTests.cs`, replace each of the following (again, drop the `"UTC",` positional argument):
```csharp
new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None),
```
→ (appears for `Handle_EventNotFound_ReturnsNotFound`)
```csharp
new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
```
```csharp
new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, "UTC", null, null, null, CalendarRecurrences.None),
```
→ (`Handle_NotOwner_ReturnsForbidden`)
```csharp
new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
```
```csharp
new UpdateCalendarEventCommand(EventId, "New Title", "New desc", Start.AddHours(2), Start.AddHours(3), false, "UTC", "Room 1", null, "#ff0000", CalendarRecurrences.Weekly),
```
→ (`Handle_Owner_UpdatesFields`)
```csharp
new UpdateCalendarEventCommand(EventId, "New Title", "New desc", Start.AddHours(2), Start.AddHours(3), false, "Room 1", null, "#ff0000", CalendarRecurrences.Weekly),
```
```csharp
new UpdateCalendarEventCommand(EventId, "New Title", null, Start.AddHours(2), Start.AddHours(3), false, "UTC", null, null, null, CalendarRecurrences.None),
```
→ (`Handle_OwnerWithParticipants_NotifiesThem`)
```csharp
new UpdateCalendarEventCommand(EventId, "New Title", null, Start.AddHours(2), Start.AddHours(3), false, null, null, null, CalendarRecurrences.None),
```
```csharp
new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(-1), false, "UTC", null, null, null, CalendarRecurrences.None),
```
→ (`Handle_EndDateBeforeStartDate_ReturnsFailure`)
```csharp
new UpdateCalendarEventCommand(EventId, "Title", null, Start, Start.AddHours(-1), false, null, null, null, CalendarRecurrences.None),
```

In `EditRecurringOccurrenceCommandHandlerTests.cs`, the single shared `MakeCommand` helper:
```csharp
    private EditRecurringOccurrenceCommand MakeCommand(RecurrenceEditScope scope) => new(
        MasterId, OriginalStart, scope, "New Title", "New desc",
        OriginalStart.AddHours(1), OriginalStart.AddHours(1).AddMinutes(30), false, "UTC", "Room 2", null, "#ff0000");
```
→
```csharp
    private EditRecurringOccurrenceCommand MakeCommand(RecurrenceEditScope scope) => new(
        MasterId, OriginalStart, scope, "New Title", "New desc",
        OriginalStart.AddHours(1), OriginalStart.AddHours(1).AddMinutes(30), false, "Room 2", null, "#ff0000");
```

- [ ] **Step 8: Add new tests for the timezone-anchoring behavior**

Add to `CreateCalendarEventCommandHandlerTests.cs` (add a `Mock<ICalendarTimezoneResolver> _timezoneResolver` field, wire into `BuildSut()`'s constructor call and every direct `new CreateCalendarEventCommandHandler(...)` call site, and stub `_timezoneResolver.Setup(x => x.ResolveForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Colombo");` in `BuildSut()`):
```csharp
    [Fact]
    public async Task Handle_SetsTimezoneFromResolver()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreateCalendarEventCommand("Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None, []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e => e.Timezone == "Asia/Colombo"), It.IsAny<CancellationToken>()), Times.Once);
    }
```

Add to `UpdateCalendarEventCommandHandlerTests.cs`:
```csharp
    [Fact]
    public async Task Handle_Owner_DoesNotChangeTimezone()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30), Timezone = "Asia/Colombo" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start.AddHours(2), Start.AddHours(3), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.Equal("Asia/Colombo", existing.Timezone);
    }
```

Add to `EditRecurringOccurrenceCommandHandlerTests.cs`:
```csharp
    [Fact]
    public async Task Handle_AllEvents_DoesNotChangeTimezone()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.Timezone = "Asia/Colombo";
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.Equal("Asia/Colombo", master.Timezone);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NewDetachedChild_InheritsMasterTimezone()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.Timezone = "Asia/Colombo";
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e => e.Timezone == "Asia/Colombo"), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 9: Run the full Calendar test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Calendar" --nologo`
Expected: all pass, including the 3 new tests and every updated call site compiling cleanly.

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/ src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs tests/ONEVO.Tests.Unit/Features/Calendar/
git commit -m "feat(calendar): anchor Timezone to the organizer at creation, freeze on every edit path"
```

---

### Task 3: Full verification

- [ ] **Step 1: Run the full unit and architecture suites**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo` then `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass, zero new failures.

- [ ] **Step 2: Build the API project**

Stop the running backend dev server first. Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Start the backend and confirm it boots cleanly**

Restart the backend dev server, check logs for a clean startup (no DI resolution error - `ICalendarTimezoneResolver` must resolve for both `CreateCalendarEventCommandHandler` and `GetMyEffectiveTimezoneQueryHandler`).

- [ ] **Step 4: Manual E2E**

Authenticated as the seeded `dapi` tenant owner:
1. `GET /api/v1/calendar/my-timezone` - confirm it returns `{"timezone": "..."}` matching whatever the seeded employee's `DisplayTimezone` or legal entity's `Timezone` actually is (check via the Personal Information / General Settings screens if unsure which value to expect).
2. `POST /api/v1/calendar` to create an event - confirm the response's `timezone` field is the resolved value from step 1, not `null` and not whatever (if anything) was sent in the request body.
3. `PUT /api/v1/calendar/{id}` to update that event's title - confirm the response's `timezone` field is unchanged from step 2.
