# Calendar Invites, RSVP, and Conflict Detection — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Notify participants (in-app + email) when added/updated/cancelled on a Calendar event, let them Accept/Decline, expose participants in the read model, and warn (non-blocking) on scheduling conflicts.

**Architecture:** In-app notifications and invite emails both go through this codebase's existing Outbox pattern (`IOutboxWriter.EnqueueAsync` → a registered `IOutboxMessageHandler`), matching exactly how `work_project_member_accepted` notifications and `password_reset` emails already work here — no new infrastructure, only new payload types, template entries, and handler registrations. A new `ICalendarNotificationSender` centralizes the "for each participant, enqueue notification + email" logic so it's written once and called from all five Calendar write handlers that need it.

**Tech Stack:** .NET 10, MediatR, EF Core, the existing Outbox/`IEmailService`/`INotificationDispatcher` infrastructure.

**Spec:** `docs/superpowers/specs/2026-08-31-calendar-invites-rsvp-conflicts-design.md`. This plan implements Parts 1-4 (backend). Part 5 (frontend) is a separate plan in the frontend repo, written after this one ships.

## Global Constraints

- Branch off `feature/calendar-recurrence-engine` (the latest shipped backend Calendar work).
- In-app notifications: `IOutboxWriter.EnqueueAsync<TPayload>(string type, TPayload payload, Guid? tenantId, CancellationToken ct)` with `type = OutboxMessageTypes.WorkNotification` and a `WorkNotificationPayload(Guid TenantId, Guid RecipientUserId, string TemplateCode, Dictionary<string,string> Placeholders, string? RelatedEntityType, Guid? RelatedEntityId)` (namespace `ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers` - despite the "WorkManagement" namespace this is the actual generic, repo-wide notification payload type; every other feature area, e.g. `AcceptObjectiveInvitationCommandHandler`, already imports it the same way).
- Invite emails: a **new**, separate outbox message type + payload + handler (`CalendarEventInviteEmailOutboxHandler`), matching `PasswordResetEmailOutboxHandler`'s exact shape - never call `IEmailService` directly from a command handler.
- `IOutboxMessageHandler` implementations are registered one-by-one in `src/ONEVO.Application/DependencyInjection.cs` (`services.AddScoped<IOutboxMessageHandler, XOutboxHandler>()`) - not auto-discovered.
- Email template bodies are hardcoded C# switch cases in `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs` - not DB-seeded, not files on disk.
- `NotificationTemplate` rows are seeded via `NotificationTemplateSeeder`'s existing idempotent-by-`Code` list.
- Conflict detection never blocks a save.
- Every multi-row write stays inside `IUnitOfWork.ExecuteInTransactionAsync`.

---

### Task 1: Participants in the read model

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/DTOs/Responses/CalendarEventResponse.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Queries/GetCalendarEvents/GetCalendarEventsQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventRepositoryTests.cs`, `tests/ONEVO.Tests.Unit/Features/Calendar/GetCalendarEventsQueryHandlerTests.cs`

**Interfaces:**
- Produces: `CalendarEventParticipantSummary(Guid EmployeeId, string EmployeeName, string ResponseStatus)`, `CalendarEventItem.Participants : IReadOnlyList<CalendarEventParticipantSummary>` (trailing defaulted param, empty list default - same non-breaking pattern used for the earlier `IsRecurringOccurrence`/`RecurrenceMasterId`/`OriginalStart` additions), `ICalendarEventRepository.GetParticipantsForEventsAsync(tenantId, IReadOnlyList<Guid> eventIds, ct)`.

- [ ] **Step 1: Widen `CalendarEventItem`/`CalendarEventsResponse`**

Add to `CalendarEventResponse.cs`:
```csharp
public sealed record CalendarEventParticipantSummary(Guid EmployeeId, string EmployeeName, string ResponseStatus);
```
Add a trailing defaulted parameter to `CalendarEventItem`'s existing positional record:
```csharp
    IReadOnlyList<CalendarEventParticipantSummary>? Participants = null);
```
(Keep every existing parameter unchanged - this is purely additive, matching the earlier `IsRecurringOccurrence = false` etc. pattern, so `CreateCalendarEventCommandHandler`/`UpdateCalendarEventCommandHandler`/`EditRecurringOccurrenceCommandHandler` keep compiling unchanged.)

- [ ] **Step 2: Write the failing repository test**

Add to `EfCalendarEventRepositoryTests.cs`:
```csharp
    [Fact]
    public async Task GetParticipantsForEventsAsync_ReturnsParticipantsGroupedByEventId()
    {
        await using var db = BuildInMemoryDb(new Mock<ICurrentUser>().Object);
        var event1 = Guid.NewGuid();
        var event2 = Guid.NewGuid();
        db.CalendarEventParticipants.AddRange(
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = event1, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Pending },
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = event1, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Accepted },
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = event2, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Pending }
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfCalendarEventRepository(db);
        var result = await repository.GetParticipantsForEventsAsync(TenantId, [event1, event2], CancellationToken.None);

        Assert.Equal(2, result[event1].Count);
        Assert.Single(result[event2]);
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetParticipantsForEventsAsync" --nologo`
Expected: FAIL - method does not exist yet.

- [ ] **Step 4: Add the repository method**

Add to `ICalendarEventRepository.cs`:
```csharp
    /// <summary>Every participant row for the given events, grouped by EventId - used by
    /// GetCalendarEventsQueryHandler to attach participant summaries to each returned item.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventParticipant>>> GetParticipantsForEventsAsync(
        Guid tenantId, IReadOnlyList<Guid> eventIds, CancellationToken ct = default);
```

Add to `EfCalendarEventRepository.cs`:
```csharp
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventParticipant>>> GetParticipantsForEventsAsync(
        Guid tenantId, IReadOnlyList<Guid> eventIds, CancellationToken ct = default)
    {
        var rows = await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && eventIds.Contains(p.EventId))
            .ToListAsync(ct);
        return rows.GroupBy(p => p.EventId).ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEventParticipant>)g.ToList());
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetParticipantsForEventsAsync" --nologo`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 6: Write the failing query-handler test**

Add to `GetCalendarEventsQueryHandlerTests.cs` (needs a `Mock<IEmployeeRepository>` call for name resolution - reuse the existing `_employees` mock already in this test class):
```csharp
    [Fact]
    public async Task Handle_AttachesParticipantSummariesToRealRows()
    {
        var sut = BuildSut();
        var eventId = Guid.NewGuid();
        var participantEmployeeId = Guid.NewGuid();
        _events.Setup(x => x.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEvent { Id = eventId, TenantId = TenantId, Title = "Standup", StartDate = From, EndDate = From.AddHours(1), SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.None, CreatedById = UserId, CreatedAt = DateTimeOffset.UtcNow }
            ]);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(eventId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [eventId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = eventId, EmployeeId = participantEmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending }]
            });
        _employees.Setup(x => x.GetByIdAsync(TenantId, participantEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = participantEmployeeId, TenantId = TenantId, FirstName = "Ada", LastName = "Lovelace" });

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = result.Value!.Events.Single();
        Assert.Single(item.Participants!);
        Assert.Equal("Ada Lovelace", item.Participants![0].EmployeeName);
        Assert.Equal(CalendarEventParticipantStatuses.Pending, item.Participants[0].ResponseStatus);
    }
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Handle_AttachesParticipantSummariesToRealRows" --nologo`
Expected: FAIL - `Participants` is always null/empty right now.

- [ ] **Step 8: Wire participants into the handler**

In `GetCalendarEventsQueryHandler.cs`, after building `items` (the real-rows mapping) and before the masters-expansion loop, insert:
```csharp
        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, items.Select(i => i.Id).ToList(), ct);
        var allParticipantEmployeeIds = participantsByEvent.Values.SelectMany(v => v).Select(p => p.EmployeeId).Distinct().ToList();
        var employeeNames = new Dictionary<Guid, string>();
        foreach (var employeeId in allParticipantEmployeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is not null) employeeNames[employeeId] = $"{employee.FirstName} {employee.LastName}";
        }

        items = items.Select(item =>
        {
            if (!participantsByEvent.TryGetValue(item.Id, out var participants)) return item;
            var summaries = participants.Select(p => new CalendarEventParticipantSummary(
                p.EmployeeId, employeeNames.GetValueOrDefault(p.EmployeeId, "Unknown"), p.ResponseStatus)).ToList();
            return item with { Participants = summaries };
        }).ToList();
```
(This runs before the recurring-masters loop adds virtual occurrences to `items` - virtual occurrences don't get their own `Participants` set here since they aren't in the `participantsByEvent` lookup by their synthetic id; Step 9 fixes that by also attaching the master's participants when synthesizing each virtual occurrence.)

- [ ] **Step 9: Attach the master's participants to virtual occurrences too**

In the same handler's masters-expansion loop, change the `items.Add(new CalendarEventItem(...))` call to also fetch and pass the master's participants (fetched once per master, outside the per-occurrence inner loop, right after `var children = await events.GetChildrenForMasterAsync(...)`):
```csharp
            var masterParticipants = await events.GetParticipantsForEventsAsync(tenantId, [master.Id], ct);
            var masterParticipantSummaries = masterParticipants.TryGetValue(master.Id, out var mp)
                ? await ResolveParticipantSummariesAsync(mp, tenantId, ct)
                : [];
```
Add a small private helper (used by both Step 8's inline loop and this one - refactor Step 8's inline block to call it too, so employee-name resolution isn't duplicated):
```csharp
    private async Task<IReadOnlyList<CalendarEventParticipantSummary>> ResolveParticipantSummariesAsync(
        IReadOnlyList<Domain.Features.Calendar.Entities.CalendarEventParticipant> participants, Guid tenantId, CancellationToken ct)
    {
        var summaries = new List<CalendarEventParticipantSummary>();
        foreach (var p in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, p.EmployeeId, ct);
            summaries.Add(new CalendarEventParticipantSummary(p.EmployeeId, employee is null ? "Unknown" : $"{employee.FirstName} {employee.LastName}", p.ResponseStatus));
        }
        return summaries;
    }
```
Then pass `Participants: masterParticipantSummaries` in the virtual-occurrence `CalendarEventItem` constructor call. (Simplify Step 8 to call this same helper instead of its own inline loop, once this exists - do this consolidation now rather than leaving two copies of the same resolution logic.)

- [ ] **Step 10: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Calendar" --nologo`
Expected: all Calendar tests pass, including the new one.

- [ ] **Step 11: Widen the API contract**

In `CalendarContracts.cs`, add:
```csharp
public sealed record CalendarEventParticipantSummaryViewModel(Guid EmployeeId, string EmployeeName, string ResponseStatus);
```
Add a trailing param to `CalendarEventViewModel`: `IReadOnlyList<CalendarEventParticipantSummaryViewModel>? Participants = null);` and update `ToViewModel(this CalendarEventItem dto)` to map `dto.Participants?.Select(p => new CalendarEventParticipantSummaryViewModel(p.EmployeeId, p.EmployeeName, p.ResponseStatus)).ToList()`.

- [ ] **Step 12: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs tests/ONEVO.Tests.Unit/Features/Calendar/
git commit -m "feat(calendar): expose participants in GetCalendarEventsQuery"
```

---

### Task 2: Email outbox handler + template + notification templates

**Files:**
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs`
- Create: `src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarEventInviteEmailOutboxHandler.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`

**Interfaces:**
- Produces: `OutboxMessageTypes.CalendarEventInviteEmail`, `CalendarEventInviteEmailPayload`, `IEmailService.SendCalendarEventInviteAsync(...)`, 3 new `NotificationTemplate` codes.

- [ ] **Step 1: Add the outbox message type constant**

In `IOutboxMessageHandler.cs`'s `OutboxMessageTypes` class, add:
```csharp
    public const string CalendarEventInviteEmail = "calendar_event_invite_email";
```

- [ ] **Step 2: Add the `IEmailService` method and implementation**

Add to `IEmailService.cs`:
```csharp
    Task SendCalendarEventInviteAsync(string to, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? location, string organizerName, CancellationToken ct = default);
```

Add to `TransactionalEmailService.cs`:
```csharp
    public Task SendCalendarEventInviteAsync(string to, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? location, string organizerName, CancellationToken ct = default)
        => SendTemplateAsync(to, "calendar_event_invite", new { recipientName, eventTitle, startDateUtc, location, organizerName }, ct);
```

- [ ] **Step 3: Add the email template**

In `EmailTemplateRenderer.cs`, add a case to the `Render` switch: `"calendar_event_invite" => RenderCalendarEventInvite(fields),` and the method:
```csharp
    private RenderedEmail RenderCalendarEventInvite(IReadOnlyDictionary<string, object?> f)
    {
        var recipientName = Get(f, "recipientName");
        var eventTitle = Get(f, "eventTitle");
        var startDateUtc = Get(f, "startDateUtc");
        var location = Get(f, "location", fallback: "");
        var organizerName = Get(f, "organizerName");

        var subject = $"You're invited: {eventTitle}";
        var locationLine = string.IsNullOrWhiteSpace(location) ? "" : $"<p>Location: {Escape(location)}</p>";
        var html = $"""
            <!doctype html><html><body>
              <p>Hi {Escape(recipientName)},</p>
              <p>{Escape(organizerName)} added you to <strong>{Escape(eventTitle)}</strong>, starting {Escape(startDateUtc)}.</p>
              {locationLine}
            </body></html>
            """;
        var text = $"Hi {recipientName},\n{organizerName} added you to \"{eventTitle}\", starting {startDateUtc}.{(string.IsNullOrWhiteSpace(location) ? "" : $"\nLocation: {location}")}";
        return new RenderedEmail(subject, html, text);
    }
```

- [ ] **Step 4: Write the outbox handler**

```csharp
// src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarEventInviteEmailOutboxHandler.cs
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

public sealed record CalendarEventInviteEmailPayload(
    Guid TenantId,
    string ToEmail,
    string RecipientName,
    string EventTitle,
    DateTimeOffset StartDateUtc,
    string? Location,
    string OrganizerName);

/// <summary>Sends the calendar-event-invite email from the outbox. Safe to retry: resending an
/// invite email for the same event is harmless.</summary>
public sealed class CalendarEventInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public CalendarEventInviteEmailOutboxHandler(IEmailService emailService) => _emailService = emailService;

    public string Type => OutboxMessageTypes.CalendarEventInviteEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarEventInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_event_invite_email payload is empty.");

        await _emailService.SendCalendarEventInviteAsync(
            payload.ToEmail, payload.RecipientName, payload.EventTitle, payload.StartDateUtc, payload.Location, payload.OrganizerName, ct);
    }
}
```

- [ ] **Step 5: Register the outbox handler**

In `src/ONEVO.Application/DependencyInjection.cs`, alongside the other `services.AddScoped<IOutboxMessageHandler, ...>()` lines:
```csharp
        services.AddScoped<IOutboxMessageHandler, ONEVO.Application.Features.Calendar.OutboxHandlers.CalendarEventInviteEmailOutboxHandler>();
```

- [ ] **Step 6: Seed the three notification templates**

In `NotificationTemplateSeeder.cs`, add three entries to the `templates` list (before the closing `};`):
```csharp
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_participant_added",
                InAppTitleTemplate = "Added to an event",
                InAppBodyTemplate = "{{organizerName}} added you to \"{{eventTitle}}\" on {{eventDate}}."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_updated",
                InAppTitleTemplate = "Event updated",
                InAppBodyTemplate = "{{organizerName}} updated \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_cancelled",
                InAppTitleTemplate = "Event cancelled",
                InAppBodyTemplate = "{{organizerName}} cancelled \"{{eventTitle}}\"."
            }
```

- [ ] **Step 7: Build to confirm everything compiles**

Stop the running backend dev server first. Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs src/ONEVO.Infrastructure/ExternalServices/Email/ src/ONEVO.Application/Features/Calendar/OutboxHandlers/ src/ONEVO.Application/DependencyInjection.cs src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs
git commit -m "feat(calendar): add invite-email outbox handler, template, and notification templates"
```

---

### Task 3: `ICalendarNotificationSender`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Services/ICalendarNotificationSender.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Services/CalendarNotificationSender.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarNotificationSenderTests.cs`

**Interfaces:**
- Consumes: `IOutboxWriter` (existing), `IEmployeeRepository.GetByIdAsync` (existing).
- Produces: `ICalendarNotificationSender` with `NotifyParticipantsAddedAsync`, `NotifyEventUpdatedAsync`, `NotifyEventCancelledAsync`.

- [ ] **Step 1: Write the interface**

```csharp
// src/ONEVO.Application/Features/Calendar/Services/ICalendarNotificationSender.cs
namespace ONEVO.Application.Features.Calendar.Services;

public interface ICalendarNotificationSender
{
    /// <summary>In-app notification + invite email to each newly-added participant.</summary>
    Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only (no email) to each participant that an event changed.</summary>
    Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only (no email) to each participant that an event was cancelled.</summary>
    Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CalendarNotificationSenderTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarNotificationSenderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private CalendarNotificationSender BuildSut()
    {
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });
        return new CalendarNotificationSender(_outbox.Object, _employees.Object);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_EnqueuesInAppNotificationAndEmail()
    {
        var sut = BuildSut();
        await sut.NotifyParticipantsAddedAsync(TenantId, "Standup", DateTimeOffset.UtcNow, "Room 4", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == UserId && p.TemplateCode == "calendar_event_participant_added"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyEventUpdatedAsync_EnqueuesOnlyInAppNotification()
    {
        var sut = BuildSut();
        await sut.NotifyEventUpdatedAsync(TenantId, "Standup", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "calendar_event_updated"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyEventCancelledAsync_EnqueuesOnlyInAppNotification()
    {
        var sut = BuildSut();
        await sut.NotifyEventCancelledAsync(TenantId, "Standup", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "calendar_event_cancelled"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsAnEmployeeIdThatNoLongerResolves()
    {
        var sut = BuildSut();
        var missingId = Guid.NewGuid();
        _employees.Setup(x => x.GetByIdAsync(TenantId, missingId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        await sut.NotifyEventUpdatedAsync(TenantId, "Standup", [missingId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarNotificationSenderTests" --nologo`
Expected: FAIL - `CalendarNotificationSender` does not exist yet.

- [ ] **Step 4: Write the implementation**

```csharp
// src/ONEVO.Application/Features/Calendar/Services/CalendarNotificationSender.cs
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarNotificationSender(IOutboxWriter outboxWriter, IEmployeeRepository employees) : ICalendarNotificationSender
{
    public async Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(
                    tenantId, employee.UserId, "calendar_event_participant_added",
                    new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle, ["eventDate"] = startDate.ToString("u") },
                    "calendar_event", null),
                tenantId, ct);

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.CalendarEventInviteEmail,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, $"{employee.FirstName} {employee.LastName}", eventTitle, startDate, location, organizerName),
                tenantId, ct);
        }
    }

    public async Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_updated", eventTitle, employeeIds, organizerName, ct);

    public async Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct = default)
        => await NotifyInAppOnlyAsync(tenantId, "calendar_event_cancelled", eventTitle, employeeIds, organizerName, ct);

    private async Task NotifyInAppOnlyAsync(
        Guid tenantId, string templateCode, string eventTitle, IReadOnlyList<Guid> employeeIds, string organizerName, CancellationToken ct)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await outboxWriter.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(
                    tenantId, employee.UserId, templateCode,
                    new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle },
                    "calendar_event", null),
                tenantId, ct);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CalendarNotificationSenderTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, alongside `ICalendarRecurrenceExpander`'s registration:
```csharp
        services.AddScoped<ICalendarNotificationSender, CalendarNotificationSender>();
```
(add `using ONEVO.Application.Features.Calendar.Services;` to the using block)

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Services/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarNotificationSenderTests.cs
git commit -m "feat(calendar): add ICalendarNotificationSender"
```

---

### Task 4: Wire notifications into the five write handlers

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/CancelRecurringOccurrenceCommandHandler.cs`
- Test: the five existing `*CommandHandlerTests.cs` files, one new test each.

**Interfaces:**
- Consumes: `ICalendarNotificationSender` (Task 3).

For each handler below, inject `ICalendarNotificationSender notifications` via the primary constructor (matching the existing `events`/`unitOfWork` injection style), and add the notification call **inside** the transaction, right before `return Result...Success(...)`. The organizer's display name is resolved via `currentUser`-adjacent lookup - since `ICurrentUser` has no display-name property, fetch the caller's own `Employee` row the same way `GetCalendarEventsQueryHandler` already does (`await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct)`), inject `IEmployeeRepository` into each handler too, and build `$"{callerEmployee?.FirstName} {callerEmployee?.LastName}".Trim()` falling back to `"Someone"` if null.

- [ ] **Step 1: `CreateCalendarEventCommandHandler`**

Add `IEmployeeRepository employees, ICalendarNotificationSender notifications` to the constructor. After the `AddParticipantsAsync` call (only `if (request.ParticipantEmployeeIds.Count > 0)`), before `return Result<CalendarEventItem>.Success(...)`:
```csharp
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyParticipantsAddedAsync(
                    tenantId, calendarEvent.Title, calendarEvent.StartDate, calendarEvent.Location,
                    request.ParticipantEmployeeIds, organizerName, innerCt);
```

Add a test to `CreateCalendarEventCommandHandlerTests.cs` verifying `_notifications.Verify(x => x.NotifyParticipantsAddedAsync(...), Times.Once)` when participants are present, and `Times.Never` when the participant list is empty (add a `Mock<ICalendarNotificationSender> _notifications` and `Mock<IEmployeeRepository> _employees` field, wire into `BuildSut()`).

- [ ] **Step 2: `UpdateCalendarEventCommandHandler`**

Add the same two dependencies. Before `return Result<CalendarEventItem>.Success(...)`, notify every EXISTING participant of the update (fetch them via a new `ICalendarEventRepository.GetParticipantsForEventsAsync(tenantId, [existing.Id], ct)` call, mapping to employee ids):
```csharp
                var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt);
                var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
                if (participantEmployeeIds.Count > 0)
                {
                    var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                    var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                    await notifications.NotifyEventUpdatedAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, innerCt);
                }
```

Add a test verifying `NotifyEventUpdatedAsync` is called once when the event has participants.

- [ ] **Step 3: `DeleteCalendarEventCommandHandler`**

Add the same two dependencies (this handler has no `ExecuteInTransactionAsync` wrapper - keep it that way, matching the plan's existing "single Remove+Save is already atomic" note; add the notification call right before `events.Remove(existing)`):
```csharp
        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], ct);
        var participantEmployeeIds = participantsByEvent.TryGetValue(existing.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
        if (participantEmployeeIds.Count > 0)
        {
            var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
            var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
            await notifications.NotifyEventCancelledAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, ct);
        }
```

Add a corresponding test.

- [ ] **Step 4: `EditRecurringOccurrenceCommandHandler`**

Add the same two dependencies. In `EditAllEventsAsync` only (not `EditThisEventOnlyAsync` - a detached occurrence has no participants per the spec's documented limitation, and not `EditThisAndFollowingAsync` since it has no UI entry point yet), before `return Result<CalendarEventItem>.Success(...)`:
```csharp
        var participantsByEvent = await events.GetParticipantsForEventsAsync(master.TenantId, [master.Id], ct);
        var participantEmployeeIds = participantsByEvent.TryGetValue(master.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
        if (participantEmployeeIds.Count > 0)
        {
            var callerEmployee = await employees.GetDefaultForUserAsync(master.TenantId, currentUser.UserId, ct);
            var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
            await notifications.NotifyEventUpdatedAsync(master.TenantId, master.Title, participantEmployeeIds, organizerName, ct);
        }
```

Add a corresponding test to the `AllEvents` test case.

- [ ] **Step 5: `CancelRecurringOccurrenceCommandHandler`**

Add the same two dependencies. Before `return Result.Success()`, notify the master's participants that this occurrence was cancelled:
```csharp
        var participantsByEvent = await events.GetParticipantsForEventsAsync(tenantId, [master.Id], ct);
        var participantEmployeeIds = participantsByEvent.TryGetValue(master.Id, out var p) ? p.Select(x => x.EmployeeId).ToList() : [];
        if (participantEmployeeIds.Count > 0)
        {
            var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
            var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
            await notifications.NotifyEventCancelledAsync(tenantId, master.Title, participantEmployeeIds, organizerName, ct);
        }
```

Add a corresponding test.

- [ ] **Step 6: Run the full Calendar test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Calendar" --nologo`
Expected: all pass, including the 5 new notification-call assertions.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/ tests/ONEVO.Tests.Unit/Features/Calendar/
git commit -m "feat(calendar): notify participants on create/update/delete/edit-occurrence/cancel-occurrence"
```

---

### Task 5: `RespondToCalendarEventCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommand.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs`, plus one repository test.

**Interfaces:**
- Produces: `RespondToCalendarEventCommand(Guid EventId, string ResponseStatus) : IRequest<Result>`, `ICalendarEventRepository.GetTrackedParticipantAsync(tenantId, eventId, employeeId, ct)`.

- [ ] **Step 1: Add the repository method**

Add to `ICalendarEventRepository.cs`:
```csharp
    /// <summary>The tracked participant row for one (event, employee) pair, or null if that
    /// employee isn't a participant on this event.</summary>
    Task<CalendarEventParticipant?> GetTrackedParticipantAsync(Guid tenantId, Guid eventId, Guid employeeId, CancellationToken ct = default);
```
Add to `EfCalendarEventRepository.cs`:
```csharp
    public async Task<CalendarEventParticipant?> GetTrackedParticipantAsync(Guid tenantId, Guid eventId, Guid employeeId, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.FirstOrDefaultAsync(
            p => p.TenantId == tenantId && p.EventId == eventId && p.EmployeeId == employeeId, ct);
```

- [ ] **Step 2: Write the command and failing tests**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;

public sealed record RespondToCalendarEventCommand(Guid EventId, string ResponseStatus) : IRequest<Result>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RespondToCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private RespondToCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employeeRepo.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId });
        return new RespondToCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employeeRepo.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_CallerNotAParticipant_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventParticipant?)null);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Accepted"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidParticipant_UpdatesResponseStatus()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Accepted"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, participant.ResponseStatus);
    }

    [Fact]
    public async Task Handle_ReAnsweringAlreadyDecidedInvitation_Succeeds()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Accepted };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Rejected"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventParticipantStatuses.Rejected, participant.ResponseStatus);
    }

    [Fact]
    public async Task Handle_InvalidResponseStatus_ReturnsFailure()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Maybe"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RespondToCalendarEventCommandHandlerTests" --nologo`
Expected: FAIL - handler does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;

public sealed class RespondToCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IEmployeeRepository employees,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RespondToCalendarEventCommand, Result>
{
    private static readonly string[] ValidStatuses = [CalendarEventParticipantStatuses.Accepted, CalendarEventParticipantStatuses.Rejected];

    public async Task<Result> Handle(RespondToCalendarEventCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        if (!ValidStatuses.Contains(request.ResponseStatus))
            return Result.Failure("ResponseStatus must be 'Accepted' or 'Rejected'.", 400);

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result.Forbidden("No employee record for the current user.");

        var participant = await events.GetTrackedParticipantAsync(tenantId, request.EventId, employee.Id, ct);
        if (participant is null)
            return Result.NotFound("You are not a participant on this event.");

        participant.ResponseStatus = request.ResponseStatus;
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RespondToCalendarEventCommandHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Add the controller endpoint**

Add to `CalendarContracts.cs`:
```csharp
public sealed record RespondToCalendarEventRequest(string ResponseStatus);
```
Add to `CalendarController.cs` (with `using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;`):
```csharp
    [HttpPost("{id:guid}/respond")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToCalendarEventRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RespondToCalendarEventCommand(id, request.ResponseStatus), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
(Uses `calendar:read`, not `calendar:write` - a participant responding to their own invitation isn't "writing" the event itself, matching the self-service-permission spirit already used elsewhere, e.g. `leave:read-own`.)

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/ src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs
git commit -m "feat(calendar): add RespondToCalendarEventCommand (Accept/Decline)"
```

---

### Task 6: `CheckCalendarConflictsQuery`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQuery.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarRecurrenceExpander` (existing).
- Produces: `CheckCalendarConflictsQuery`, `CalendarConflict(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle)`, `CalendarConflictsResponse`, `ICalendarEventRepository.GetInDateRangeForEmployeeAsync`/`GetRecurringMastersForEmployeeAsync`.

- [ ] **Step 1: Add the two employee-scoped repository methods**

Add to `ICalendarEventRepository.cs`:
```csharp
    /// <summary>Same shape as GetInDateRangeForCallerAsync, but scoped to a specific employee's
    /// participation/creation rather than the current caller - used for conflict-checking a
    /// participant who is not the person making the request.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Same shape as GetRecurringMastersForCallerAsync, scoped to a specific employee.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset to, CancellationToken ct = default);
```

Add to `EfCalendarEventRepository.cs` (both derived from the existing caller-scoped methods, replacing the `CreatedById == userId OR participant` check with a pure participant-or-creator-by-employeeId check - note the existing methods key off `CreatedById` which is a **UserId**, so an employee's own created events need that employee's UserId, resolved by the caller before invoking these; simplest correct approach: match purely on participation for these employee-scoped variants, since "is this employee busy" for conflict-checking should include events they created too - resolve the employee's UserId once in the query handler and pass it alongside):
```csharp
    public async Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && !e.IsRecurrenceCancelled
                        && (e.RecurrenceParentId != null || e.Recurrence == CalendarRecurrences.None)
                        && e.StartDate <= to && e.EndDate >= from
                        && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))
            .OrderBy(e => e.StartDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.Recurrence != CalendarRecurrences.None
                        && e.RecurrenceParentId == null
                        && e.StartDate <= to
                        && _db.CalendarEventParticipants.Any(p => p.EventId == e.Id && p.EmployeeId == employeeId))
            .ToListAsync(ct);
    }
```
(Deliberately participant-only, not creator-only-by-UserId: an employee who created an event is always also free to be checked as "busy" via being a participant if they added themselves, but this spec's conflict check is specifically about *invited* participants' availability - a simpler, single-condition query. Note this in a comment on both methods rather than silently narrowing scope without explanation.)

- [ ] **Step 2: Write the query and failing tests**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

public sealed record CalendarConflict(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle);
public sealed record CalendarConflictsResponse(IReadOnlyList<CalendarConflict> Conflicts);

public sealed record CheckCalendarConflictsQuery(
    IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate)
    : IRequest<Result<CalendarConflictsResponse>>;
```

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CheckCalendarConflictsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarRecurrenceExpander> _expander = new();

    private CheckCalendarConflictsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, FirstName = "Ada", LastName = "Lovelace" });
        _events.Setup(x => x.GetRecurringMastersForEmployeeAsync(TenantId, EmployeeId, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new CheckCalendarConflictsQueryHandler(_currentUser.Object, _events.Object, _employees.Object, _expander.Object);
    }

    [Fact]
    public async Task Handle_NoOverlap_ReturnsEmptyConflictList()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task Handle_DirectOverlap_ReturnsOneConflict()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Standup", StartDate = Start, EndDate = End, SourceType = CalendarEventSourceTypes.Manual, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow }]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal("Ada Lovelace", result.Value.Conflicts[0].EmployeeName);
    }

    [Fact]
    public async Task Handle_OverlapAgainstRecurringMasterOccurrence_ReturnsOneConflict()
    {
        var sut = BuildSut();
        var master = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Weekly Sync", StartDate = Start.AddDays(-7), EndDate = End.AddDays(-7),
            SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY",
            CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetRecurringMastersForEmployeeAsync(TenantId, EmployeeId, End, It.IsAny<CancellationToken>())).ReturnsAsync([master]);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, master.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _expander.Setup(x => x.Expand("FREQ=WEEKLY", master.StartDate, Start, End)).Returns([Start]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal("Weekly Sync", result.Value.Conflicts[0].ConflictingEventTitle);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CheckCalendarConflictsQueryHandlerTests" --nologo`
Expected: FAIL - handler does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/CheckCalendarConflictsQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

public sealed class CheckCalendarConflictsQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IEmployeeRepository employees,
    ICalendarRecurrenceExpander expander)
    : IRequestHandler<CheckCalendarConflictsQuery, Result<CalendarConflictsResponse>>
{
    public async Task<Result<CalendarConflictsResponse>> Handle(CheckCalendarConflictsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CalendarConflictsResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var conflicts = new List<CalendarConflict>();

        foreach (var employeeId in request.ParticipantEmployeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;
            var employeeName = $"{employee.FirstName} {employee.LastName}";

            var realEvents = await events.GetInDateRangeForEmployeeAsync(tenantId, employeeId, request.StartDate, request.EndDate, ct);
            foreach (var e in realEvents)
                conflicts.Add(new CalendarConflict(employeeId, employeeName, e.Id, e.Title));

            var masters = await events.GetRecurringMastersForEmployeeAsync(tenantId, employeeId, request.EndDate, ct);
            foreach (var master in masters)
            {
                var occurrenceStarts = expander.Expand(master.RecurrenceRule!, master.StartDate, request.StartDate, request.EndDate);
                if (occurrenceStarts.Count == 0) continue;

                var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
                var hasUncancelledOccurrence = occurrenceStarts.Any(start =>
                    !children.Any(c => c.RecurrenceOriginalStart == start && c.IsRecurrenceCancelled));
                if (hasUncancelledOccurrence)
                    conflicts.Add(new CalendarConflict(employeeId, employeeName, master.Id, master.Title));
            }
        }

        return Result<CalendarConflictsResponse>.Success(new CalendarConflictsResponse(conflicts));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CheckCalendarConflictsQueryHandlerTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Add the controller endpoint**

Add to `CalendarContracts.cs`:
```csharp
public sealed record CheckCalendarConflictsRequest(IReadOnlyList<Guid> ParticipantEmployeeIds, DateTimeOffset StartDate, DateTimeOffset EndDate);
public sealed record CalendarConflictViewModel(Guid EmployeeId, string EmployeeName, Guid ConflictingEventId, string ConflictingEventTitle);
public sealed record CalendarConflictsViewModel(IReadOnlyList<CalendarConflictViewModel> Conflicts);
```
Add a mapper alongside the existing `CalendarEventViewModelMapper`:
```csharp
public static class CalendarConflictsViewModelMapper
{
    public static CalendarConflictsViewModel ToViewModel(this CalendarConflictsResponse dto) =>
        new(dto.Conflicts.Select(c => new CalendarConflictViewModel(c.EmployeeId, c.EmployeeName, c.ConflictingEventId, c.ConflictingEventTitle)).ToList());
}
```
Add to `CalendarController.cs` (with `using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;`):
```csharp
    [HttpPost("check-conflicts")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> CheckConflicts([FromBody] CheckCalendarConflictsRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CheckCalendarConflictsQuery(request.ParticipantEmployeeIds, request.StartDate, request.EndDate), ct);
        return result.IsSuccess ? Ok(result.Value!.ToViewModel()) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Queries/CheckCalendarConflicts/ src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs tests/ONEVO.Tests.Unit/Features/Calendar/CheckCalendarConflictsQueryHandlerTests.cs
git commit -m "feat(calendar): add CheckCalendarConflictsQuery"
```

---

### Task 7: Full verification and manual E2E

- [ ] **Step 1: Run the full unit and architecture suites**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo` then `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --nologo`
Expected: all pass, zero new failures.

- [ ] **Step 2: Build the API project**

Stop the running backend dev server first. Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: `Build succeeded. 0 Error(s)` - this catches DI registration mistakes (a missing `AddScoped` throws at startup, not at build time, so also do Step 3).

- [ ] **Step 3: Start the backend and confirm it boots without a DI resolution error**

Restart the backend dev server, check its logs for a clean startup (no `AggregateException: Some services are not able to be constructed`, matching the exact failure class Calendar Core hit once already this session when a repository registration was missed).

- [ ] **Step 4: Manual E2E**

Authenticated as the seeded `dapi` tenant owner:
1. `POST /api/v1/calendar` with one participant employee id - confirm `201`, then `GET /api/v1/notifications` (or the seeded second tenant user's own session) shows a new notification with `templateCode = "calendar_event_participant_added"`.
2. `GET /api/v1/calendar?from=&to=` for that date - confirm the returned item's `participants` array has one entry with `responseStatus: "pending"`.
3. As the participant's own session, `POST /api/v1/calendar/{eventId}/respond` with `{"responseStatus":"Accepted"}` - confirm `204`, then re-run the `GET` from step 2 and confirm `responseStatus` is now `"accepted"`.
4. `PUT /api/v1/calendar/{eventId}` to change the title - confirm a `calendar_event_updated` notification appears for the participant.
5. `DELETE /api/v1/calendar/{eventId}` - confirm a `calendar_event_cancelled` notification appears.
6. `POST /api/v1/calendar/check-conflicts` with the same participant and a time range overlapping an event they're already on - confirm one conflict entry comes back; with a non-overlapping range, confirm an empty list.
