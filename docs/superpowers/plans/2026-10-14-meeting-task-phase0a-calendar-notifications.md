# Meetings as Tasks — Phase 0A (Calendar notification delivery and email idempotency) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every calendar change reliably reaches the people it affects (participants in-app + email, guests by email), exactly once per operation, committed atomically with the change.

**Architecture:** Outbox rows are staged before the handler's single `SaveChangesAsync`, so the business change and its notifications commit together (spec §3.1). Emails that must not repeat carry a `deduplication_key`. They are inserted immediately inside the handler's open transaction with `INSERT … ON CONFLICT DO NOTHING`, so a duplicate is a silent no-op that never fails the save. One email template (`calendar_event_invite`) gains `updated`, `cancelled` and `removed` variants.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (snake_case), MediatR, xUnit + Moq (unit), Testcontainers PostgreSQL (integration, no Moq: fakes are hand-written).

**Spec:** `docs/superpowers/specs/2026-10-13-meeting-task-phase0-1-design.md` §3.1, §3.2 rows P0-1a/b/c, P0-3, P1-2, P1-3, P1-4, X-1 (invite, update and cancel keys), §3.2.2 "Atomic dedupe", §3.4.

**Phase 0 is split into three plans** (skill scope check: three independent subsystems):
- **0A (this plan):** notification delivery and email idempotency.
- **0B:** attendance sync classification (P0-2). File `2026-10-14-meeting-task-phase0b-attendance-sync.md`.
- **0C:** online-meeting provisioning, cancellation, link generation and link emails (P1-5, X-2, X-3, X-1 link keys). File `2026-10-14-meeting-task-phase0c-meeting-provisioning.md`.

0B and 0C consume Task 1's `IOutboxWriter.EnqueueKeyedAsync` and Task 2's email kinds.

## Global Constraints

- Worktrees: backend `C:\Users\User\onexso-worktrees\be-meeting-task`, frontend `C:\Users\User\onexso-worktrees\fe-meeting-task`. Both are on branch `feature/meeting-task-phase0-1`. Never edit the main checkouts. 0A changes the backend only.
- **Rule for every handler (spec §3.1):** stage all outbox rows (notifications and emails) *before* the single `SaveChangesAsync` that commits the business change.
- Keyed emails are inserted with `INSERT … ON CONFLICT (deduplication_key) DO NOTHING` **inside the handler's open transaction**. The writer **refuses** to enqueue a keyed message when no transaction is active. Unkeyed messages keep today's staging behaviour. `IIdempotencyStore` is **not** used for this.
- Email keys (spec X-1, verbatim shapes): `invite:{participantRowId|guestRowId}`, `update:{eventId}:{revision}:{recipient}`, `cancel:{eventId}:{occurrenceStart|series}:{recipient}`. `{recipient}` = the participant or guest row ID. `{occurrenceStart}` = the occurrence's original start as UTC ticks.
- **Re-invite key (this plan's ruling):** re-adding a removed participant **revives the same row** (`GetRemovedParticipantsAsync`, added after the spec was verified), so the spec's premise "row IDs are new on every add" is false for participants. A revived row's invite key is `invite:{rowId}:{previousDeletedAt.UtcTicks}`, so a re-added participant still gets a second invite, as the spec's test table requires. Guests are hard-deleted, so they always get a new row ID.
- Removal emails (participant removed, guest removed) are **unkeyed**. The spec lists no key for them, and a retried remove returns 404 before any email.
- Migrations: generate with `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`. The design-time factory needs `ConnectionStrings__MigrationConnection`. `migrations add` never connects, so pass a design-time-only value: `ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none"`.
- Production code has no mocks, stubs or placeholders. Unit tests use Moq. Integration tests use hand-written fakes.
- Commit and push to the branch only, **no PR**. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Integration tests need Docker running.

## Review Focus

1. **A participant removed and re-added** gets a second invite (revived row, new key). Tested in Task 3 (unit) and Task 9 (integration).
2. **Cancelling the same occurrence twice** must not email anyone twice (same `cancel:` key). Tested in Task 9.
3. **An email key already committed by an earlier request** must make the new insert a no-op **without** failing the new request's business change. Tested in Task 1.
4. **A keyed email enqueued with no open transaction** must throw rather than commit on its own. Tested in Task 1.
5. **"This and following" on a series with guests** copies the guests (not just participants) onto the new series. Tested in Task 5.

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Domain/Features/SharedPlatform/Entities/OutboxMessage.cs` (modify) | `DeduplicationKey` |
| `src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Outbox/OutboxMessageConfiguration.cs` (modify) | Column + unique filtered index |
| `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxWriter.cs`, `src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxWriter.cs` (modify) | `EnqueueKeyedAsync` |
| `src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarEventInviteEmailOutboxHandler.cs` (modify) | `CalendarEmailKinds`, payload `Kind` |
| `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs`, `TransactionalEmailService.cs`, `EmailTemplateRenderer.cs`, `tests/…/E2E/CapturingEmailService.cs` (modify) | Kind-aware calendar email |
| `src/ONEVO.Application/Features/Calendar/Services/CalendarRecipients.cs` (create) | Recipient records + `CalendarEmailKeys` |
| `src/ONEVO.Application/Features/Calendar/Services/{I,}CalendarNotificationSender.cs` (modify) | Recipient-based API, updated/cancelled emails, RSVP/nominee/removal notices |
| `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` (modify) | 4 new in-app templates |
| `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs` (modify) | `Revision` |
| Calendar command handlers (modify) | Create, AddParticipants, AddGuests, CreateEventMeeting (signature only), Update, EditRecurringOccurrence, CancelRecurringOccurrence, Delete, RespondToCalendarEvent, RemoveCalendarEventParticipant, RemoveCalendarEventGuest |
| `tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationDb.cs` (create) | Harness: real DbContext, repositories, OutboxWriter, sender |
| `tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationOutboxIntegrationTests.cs` (create) | Rows exist in the DB after each command returns |

---

### Task 1: Keyed outbox messages (`deduplication_key`, `EnqueueKeyedAsync`)

**Files:**
- Modify: `src/ONEVO.Domain/Features/SharedPlatform/Entities/OutboxMessage.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Outbox/OutboxMessageConfiguration.cs`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxWriter.cs`
- Modify: `src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxWriter.cs`
- Create (generated): `src/ONEVO.Infrastructure/Migrations/<ts>_AddOutboxDeduplicationKey.cs`
- Create: `tests/ONEVO.Tests.Integration/SharedPlatform/OutboxKeyedEnqueueIntegrationTests.cs`

**Interfaces:**
- Produces: `Task<bool> IOutboxWriter.EnqueueKeyedAsync<TPayload>(string type, TPayload payload, string deduplicationKey, Guid? tenantId = null, CancellationToken ct = default)`. It returns `true` when inserted and `false` when the key already exists. It throws `InvalidOperationException` when no transaction is active. Tasks 3–9 and plans 0B/0C rely on it.

- [ ] **Step 1: Write the failing integration tests**

`tests/ONEVO.Tests.Integration/SharedPlatform/OutboxKeyedEnqueueIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

public sealed class OutboxKeyedEnqueueIntegrationTests
{
    private sealed record Payload(string Value);

    private static readonly AesEncryptionService Encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private static OutboxWriter Writer(ApplicationDbContext db) => new(db, Encryption, new SystemDateTimeProvider());

    private static Task<int> CountAsync(ApplicationDbContext db, string key) =>
        db.Set<OutboxMessage>().AsNoTracking().CountAsync(m => m.DeduplicationKey == key);

    [Fact]
    public async Task SameKeyTwiceInOneTransaction_InsertsOnce_SecondIsANoOp()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var key = $"test:{Guid.NewGuid()}";

        await using var tx = await db.Database.BeginTransactionAsync();
        var first = await Writer(db).EnqueueKeyedAsync("test_type", new Payload("a"), key, h.TenantId);
        var second = await Writer(db).EnqueueKeyedAsync("test_type", new Payload("b"), key, h.TenantId);
        await tx.CommitAsync();

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, await CountAsync(db, key));
        var row = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.DeduplicationKey == key);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Contains("\"a\"", Encryption.Decrypt(row.EncryptedPayload));
    }

    [Fact]
    public async Task KeyedEnqueue_WithoutATransaction_Throws_AndInsertsNothing()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var key = $"test:{Guid.NewGuid()}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Writer(db).EnqueueKeyedAsync("test_type", new Payload("a"), key, h.TenantId));

        Assert.Equal(0, await CountAsync(db, key));
    }

    [Fact]
    public async Task KeyAlreadyCommittedByAnEarlierRequest_IsANoOp_AndTheNewBusinessChangeStillCommits()
    {
        var h = await MyTeamDb.CreateAsync();
        var key = $"test:{Guid.NewGuid()}";
        await using (var earlier = h.NewContext())
        {
            await using var tx = await earlier.Database.BeginTransactionAsync();
            await Writer(earlier).EnqueueKeyedAsync("test_type", new Payload("first"), key, h.TenantId);
            await tx.CommitAsync();
        }

        await using var db = h.NewContext();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // The "business change": an unkeyed, staged row that only SaveChanges persists.
            await Writer(db).EnqueueAsync("business_marker", new Payload("change"), h.TenantId);
            var inserted = await Writer(db).EnqueueKeyedAsync("test_type", new Payload("second"), key, h.TenantId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Assert.False(inserted);
        }

        await using var verify = h.NewContext();
        Assert.Equal(1, await CountAsync(verify, key));
        Assert.Equal(1, await verify.Set<OutboxMessage>().CountAsync(m => m.Type == "business_marker" && m.TenantId == h.TenantId));
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoKeyedRow()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var key = $"test:{Guid.NewGuid()}";

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await Writer(db).EnqueueKeyedAsync("test_type", new Payload("a"), key, h.TenantId);
            await tx.RollbackAsync();
        }

        Assert.Equal(0, await CountAsync(db, key));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `CS1061` errors (`'OutboxWriter' does not contain a definition for 'EnqueueKeyedAsync'`, `'OutboxMessage' does not contain a definition for 'DeduplicationKey'`).

- [ ] **Step 3: Add the column, index and writer method**

In `OutboxMessage.cs`, after `public string? LastError { get; set; }` add:

```csharp
    /// <summary>Optional idempotency key (unique when set). Keyed messages are inserted with
    /// INSERT … ON CONFLICT DO NOTHING inside the caller's transaction, so the same logical message
    /// (e.g. one email per recipient per operation) is queued at most once.</summary>
    public string? DeduplicationKey { get; set; }
```

In `OutboxMessageConfiguration.Configure`, after `builder.Property(m => m.LastError).HasMaxLength(2000);` add:

```csharp
        builder.Property(m => m.DeduplicationKey).HasMaxLength(200);
        builder.HasIndex(m => m.DeduplicationKey)
            .IsUnique()
            .HasFilter("deduplication_key IS NOT NULL")
            .HasDatabaseName("ix_outbox_messages_deduplication_key");
```

Replace `IOutboxWriter.cs` with:

```csharp
namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IOutboxWriter
{
    Task EnqueueAsync<TPayload>(
        string type,
        TPayload payload,
        Guid? tenantId = null,
        CancellationToken ct = default);

    /// <summary>Inserts the message immediately with INSERT … ON CONFLICT (deduplication_key) DO
    /// NOTHING, inside the caller's open transaction (IUnitOfWork.ExecuteInTransactionAsync).
    /// Returns false when a message with this key already exists - a silent no-op that never fails
    /// the caller's save. Throws InvalidOperationException when no transaction is active, so a keyed
    /// message can never commit separately from its business change.</summary>
    Task<bool> EnqueueKeyedAsync<TPayload>(
        string type,
        TPayload payload,
        string deduplicationKey,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
```

In `OutboxWriter.cs` add `using Microsoft.EntityFrameworkCore;` and this method after `EnqueueAsync`:

```csharp
    public async Task<bool> EnqueueKeyedAsync<TPayload>(
        string type,
        TPayload payload,
        string deduplicationKey,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "A keyed outbox message must be enqueued inside the handler's transaction (IUnitOfWork.ExecuteInTransactionAsync).");

        var now = _clock.UtcNow;
        var encryptedPayload = _encryption.Encrypt(JsonSerializer.Serialize(payload));
        var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_messages
                (id, type, encrypted_payload, tenant_id, status, attempt_count, created_at, next_attempt_at, deduplication_key)
            VALUES
                ({Guid.NewGuid()}, {type}, {encryptedPayload}, {tenantId}, {OutboxMessageStatus.Pending}, 0, {now}, {now}, {deduplicationKey})
            ON CONFLICT (deduplication_key) WHERE deduplication_key IS NOT NULL DO NOTHING
            """, ct);
        return inserted == 1;
    }
```

- [ ] **Step 4: Generate the migration**

```bash
cd "C:/Users/User/onexso-worktrees/be-meeting-task"
ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none" dotnet ef migrations add AddOutboxDeduplicationKey --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
git diff --stat src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
```
Expected: `Done.` The new migration's `Up` has `AddColumn<string>("deduplication_key", "outbox_messages", maxLength: 200, nullable: true)` and `CreateIndex("ix_outbox_messages_deduplication_key", …, unique: true, filter: "deduplication_key IS NOT NULL")`. The snapshot diff adds only these two.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~OutboxKeyedEnqueueIntegrationTests"`
Expected: 4 passed.

- [ ] **Step 6: Unit and architecture suites still build and pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~Outbox" && dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -nologo`
Expected: all pass. If any unit-test class implements `IOutboxWriter` by hand, add the member there with `=> Task.FromResult(true)` after recording the call the way that fake records `EnqueueAsync`. Find them with `grep -rn ": IOutboxWriter" tests`.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/SharedPlatform/Entities/OutboxMessage.cs src/ONEVO.Infrastructure/Persistence/Configurations/SharedPlatform/Outbox/OutboxMessageConfiguration.cs src/ONEVO.Application/Common/ServiceInterfaces/IOutboxWriter.cs src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxWriter.cs src/ONEVO.Infrastructure/Migrations tests
git commit -m "feat(outbox): keyed messages inserted with ON CONFLICT inside the caller's transaction

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Calendar email variants (`invite`, `updated`, `cancelled`, `removed`)

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarEventInviteEmailOutboxHandler.cs`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs:14`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs:46-47`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs:325-370`
- Modify: `tests/ONEVO.Tests.Integration/E2E/CapturingEmailService.cs:66-67`
- Modify: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarEventInviteEmailOutboxHandlerTests.cs`

**Interfaces:**
- Produces: `CalendarEmailKinds.Invite|Updated|Cancelled|Removed` (`"invite"`, `"updated"`, `"cancelled"`, `"removed"`). It also produces `CalendarEventInviteEmailPayload(…, string? MeetingLink = null, string Kind = CalendarEmailKinds.Invite)` and `IEmailService.SendCalendarEventInviteAsync(string to, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? location, string organizerName, string? meetingLink = null, string kind = "invite", CancellationToken ct = default)`. Plan 0C adds `LinkRemoved`.

- [ ] **Step 1: Write the failing tests**

Append to `EmailTemplateRendererTests`:

```csharp
    [Theory]
    [InlineData("updated", "Updated: Deployment Meeting", "Event updated")]
    [InlineData("cancelled", "Cancelled: Deployment Meeting", "Event cancelled")]
    [InlineData("removed", "Removed from: Deployment Meeting", "removed from an event")]
    public void RenderCalendarEventInvite_Kind_ChangesSubjectAndHeading(string kind, string subject, string heading)
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Ada Lovelace", eventTitle = "Deployment Meeting", startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null, organizerName = "Dapi Owner", meetingLink = "https://us05web.zoom.us/j/1", kind
        });

        rendered.Subject.Should().Be(subject);
        rendered.HtmlBody.Should().Contain(heading);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("removed")]
    public void RenderCalendarEventInvite_CancelledOrRemoved_NeverShowsTheJoinLink(string kind)
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Ada Lovelace", eventTitle = "Deployment Meeting", startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null, organizerName = "Dapi Owner", meetingLink = "https://us05web.zoom.us/j/1", kind
        });

        rendered.HtmlBody.Should().NotContain("https://us05web.zoom.us/j/1");
        rendered.TextBody.Should().NotContain("https://us05web.zoom.us/j/1");
    }

    [Fact]
    public void RenderCalendarEventInvite_NoKind_IsTheInvite()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Ada Lovelace", eventTitle = "Deployment Meeting", startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null, organizerName = "Dapi Owner"
        });

        rendered.Subject.Should().Be("You're invited: Deployment Meeting");
    }
```

`tests/ONEVO.Tests.Unit/Features/Calendar/CalendarEventInviteEmailOutboxHandlerTests.cs`:

```csharp
using System.Text.Json;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarEventInviteEmailOutboxHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_PassesTheKindThrough()
    {
        var email = new Mock<IEmailService>();
        var payload = new CalendarEventInviteEmailPayload(
            Guid.NewGuid(), "ada@example.com", "Ada", "Standup", Start, null, "Owner", null, CalendarEmailKinds.Cancelled);

        await new CalendarEventInviteEmailOutboxHandler(email.Object).HandleAsync(JsonSerializer.Serialize(payload), CancellationToken.None);

        email.Verify(e => e.SendCalendarEventInviteAsync(
            "ada@example.com", "Ada", "Standup", Start, null, "Owner", null, CalendarEmailKinds.Cancelled, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_LegacyPayloadWithoutKind_SendsTheInvite()
    {
        var email = new Mock<IEmailService>();
        const string legacyJson = """{"TenantId":"3f1d1c56-35c0-4a52-9a52-4f7c0b0d8e11","ToEmail":"ada@example.com","RecipientName":"Ada","EventTitle":"Standup","StartDateUtc":"2026-10-20T09:00:00+00:00","Location":null,"OrganizerName":"Owner","MeetingLink":null}""";

        await new CalendarEventInviteEmailOutboxHandler(email.Object).HandleAsync(legacyJson, CancellationToken.None);

        email.Verify(e => e.SendCalendarEventInviteAsync(
            "ada@example.com", "Ada", "Standup", Start, null, "Owner", null, CalendarEmailKinds.Invite, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `CalendarEmailKinds` does not exist; `SendCalendarEventInviteAsync` has no overload taking 9 arguments.

- [ ] **Step 3: Implement**

In `CalendarEventInviteEmailOutboxHandler.cs`, replace the payload record with:

```csharp
public static class CalendarEmailKinds
{
    public const string Invite = "invite";
    public const string Updated = "updated";
    public const string Cancelled = "cancelled";
    public const string Removed = "removed";
}

public sealed record CalendarEventInviteEmailPayload(
    Guid TenantId,
    string ToEmail,
    string RecipientName,
    string EventTitle,
    DateTimeOffset StartDateUtc,
    string? Location,
    string OrganizerName,
    string? MeetingLink = null,
    string Kind = CalendarEmailKinds.Invite);
```

and replace the `SendCalendarEventInviteAsync` call in `HandleAsync` with:

```csharp
        // Payloads queued before Kind existed deserialize with Kind = null: those were invites.
        var kind = string.IsNullOrEmpty(payload.Kind) ? CalendarEmailKinds.Invite : payload.Kind;
        await _emailService.SendCalendarEventInviteAsync(
            payload.ToEmail, payload.RecipientName, payload.EventTitle, payload.StartDateUtc, payload.Location,
            payload.OrganizerName, payload.MeetingLink, kind, ct);
```

`IEmailService.cs:14` becomes:

```csharp
    Task SendCalendarEventInviteAsync(string to, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? location, string organizerName, string? meetingLink = null, string kind = "invite", CancellationToken ct = default);
```

`TransactionalEmailService.cs:46-47` and `CapturingEmailService.cs:66-67` both become:

```csharp
    public Task SendCalendarEventInviteAsync(string to, string recipientName, string eventTitle, DateTimeOffset startDateUtc, string? location, string organizerName, string? meetingLink = null, string kind = "invite", CancellationToken ct = default)
        => SendTemplateAsync(to, "calendar_event_invite", new { recipientName, eventTitle, startDateUtc, location, organizerName, meetingLink, kind }, ct);
```

In `EmailTemplateRenderer.RenderCalendarEventInvite`, replace from `var subject = $"You're invited: {eventTitle}";` through the line before `var html = $"""` with:

```csharp
        var kind = Get(f, "kind", fallback: "invite");
        var (subject, heading, introHtml, introText, showJoinLink) = kind switch
        {
            "updated" => ($"Updated: {eventTitle}", "Event updated",
                $"{Escape(organizerName)} updated this event. Here are the current details:",
                $"{organizerName} updated \"{eventTitle}\". Here are the current details:", true),
            "cancelled" => ($"Cancelled: {eventTitle}", "Event cancelled",
                $"{Escape(organizerName)} cancelled this event:",
                $"{organizerName} cancelled \"{eventTitle}\".", false),
            "removed" => ($"Removed from: {eventTitle}", "You've been removed from an event",
                $"{Escape(organizerName)} removed you from this event:",
                $"{organizerName} removed you from \"{eventTitle}\".", false),
            _ => ($"You're invited: {eventTitle}", "Meeting invitation",
                $"{Escape(organizerName)} has invited you to the following event:",
                $"{organizerName} has invited you to \"{eventTitle}\".", true)
        };
        var whenRow = $"""<tr><td style="padding:4px 8px 4px 0; color:#64748b;">When</td><td style="padding:4px 0;">{Escape(startDateUtc)}</td></tr>""";
        var whereRow = string.IsNullOrWhiteSpace(location)
            ? ""
            : $"""<tr><td style="padding:4px 8px 4px 0; color:#64748b;">Where</td><td style="padding:4px 0;">{Escape(location)}</td></tr>""";
        var joinUrl = showJoinLink && !string.IsNullOrWhiteSpace(meetingLink) ? meetingLink : null;
        var meetingLinkLine = joinUrl is null
            ? ""
            : $"""<p><a href="{Escape(joinUrl)}" style="display:inline-block; padding:10px 16px; background:#0f172a; color:#fff; text-decoration:none; border-radius:6px;">Join meeting</a></p>""";
```

Then, in the HTML literal, change `<h2 style="margin:0 0 12px;">Meeting invitation</h2>` to `<h2 style="margin:0 0 12px;">{heading}</h2>`. Also change `<p>{Escape(organizerName)} has invited you to the following event:</p>` to `<p>{introHtml}</p>`. In the text literal, change `{organizerName} has invited you to "{eventTitle}".` to `{introText}`, and change `{(string.IsNullOrWhiteSpace(meetingLink) ? "" : $"Join: {meetingLink}\n")}` to `{(joinUrl is null ? "" : $"Join: {joinUrl}\n")}`. Delete the old `whenRow`, `whereRow` and `meetingLinkLine` declarations that followed the old subject line, since they are now declared above.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~EmailTemplateRendererTests|FullyQualifiedName~CalendarEventInviteEmailOutboxHandlerTests"`
Expected: all pass (existing renderer tests unchanged).
Run: `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/OutboxHandlers src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs src/ONEVO.Infrastructure/ExternalServices/Email tests/ONEVO.Tests.Integration/E2E/CapturingEmailService.cs tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarEventInviteEmailOutboxHandlerTests.cs
git commit -m "feat(calendar-email): updated, cancelled and removed variants of the event email

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Recipient-based sender, invite keys, new in-app templates

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Services/CalendarRecipients.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Services/ICalendarNotificationSender.cs`, `CalendarNotificationSender.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`, `tests/ONEVO.Tests.Unit/Features/Auth/NotificationTemplateSeederTests.cs`
- Modify (call sites): `CreateCalendarEventCommandHandler.cs`, `AddCalendarEventParticipantsCommandHandler.cs`, `AddCalendarEventGuestsCommandHandler.cs`, `CreateEventMeetingCommandHandler.cs`, `UpdateCalendarEventCommandHandler.cs`, `EditRecurringOccurrenceCommandHandler.cs`, `CancelRecurringOccurrenceCommandHandler.cs`, `DeleteCalendarEventCommandHandler.cs` (all under `src/ONEVO.Application/Features/Calendar/Commands/`)
- Rewrite: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarNotificationSenderTests.cs`
- Modify: the handler test files listed in Step 6

**Interfaces:**
- Consumes: `EnqueueKeyedAsync` (Task 1), `CalendarEmailKinds`, payload `Kind` (Task 2).
- Produces (used by Tasks 4–9):

```csharp
public sealed record CalendarParticipantRecipient(Guid EmployeeId, string? EmailKey = null);
public sealed record CalendarGuestRecipient(string Email, string? EmailKey = null);
public static class CalendarEmailKeys
{
    string Invite(Guid rowId);                                        // "invite:{rowId}"
    string ReInvite(Guid participantRowId, DateTimeOffset previousDeletedAt); // "invite:{rowId}:{ticks}"
    string Update(Guid eventId, int revision, Guid rowId);            // "update:{eventId}:{revision}:{rowId}"
    string Cancel(Guid eventId, DateTimeOffset? occurrenceStart, Guid rowId); // "cancel:{eventId}:{ticks|series}:{rowId}"
}
// ICalendarNotificationSender
Task NotifyParticipantsAddedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, CancellationToken ct = default, string? meetingLink = null, bool sendInviteEmail = true);
Task NotifyMeetingLinkAddedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default); // unchanged
Task NotifyGuestsAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, string? meetingLink, CancellationToken ct = default);
Task NotifyEventUpdatedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink, IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, CancellationToken ct = default);
Task NotifyEventCancelledAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, CancellationToken ct = default);
Task NotifyResolutionRequestedAsync(...); Task NotifyReplacementNominatedAsync(...); // unchanged
Task NotifyRsvpAsync(Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default);
Task NotifyNomineeAsync(Guid tenantId, Guid nomineeEmployeeId, string eventTitle, string responderName, CancellationToken ct = default);
Task NotifyParticipantRemovedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, Guid employeeId, string organizerName, CancellationToken ct = default);
Task NotifyGuestRemovedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string guestEmail, string organizerName, CancellationToken ct = default);
```

- [ ] **Step 1: Create the recipients and keys**

`src/ONEVO.Application/Features/Calendar/Services/CalendarRecipients.cs`:

```csharp
using System.Globalization;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>A participant to notify. When <see cref="EmailKey"/> is set the email is idempotent
/// (one per key, see <see cref="CalendarEmailKeys"/>) and must be sent inside the handler's
/// transaction; null sends an ordinary, unkeyed email.</summary>
public sealed record CalendarParticipantRecipient(Guid EmployeeId, string? EmailKey = null);

/// <summary>An external guest to email; <see cref="EmailKey"/> as for participants.</summary>
public sealed record CalendarGuestRecipient(string Email, string? EmailKey = null);

/// <summary>Email idempotency keys (spec §3.2.2 X-1). The recipient part is the participant or guest
/// row ID, so a retried operation can't email anyone twice while a genuinely new add, update or
/// cancellation still emails.</summary>
public static class CalendarEmailKeys
{
    public static string Invite(Guid rowId) => $"invite:{rowId}";

    /// <summary>Re-adding a removed participant revives the same row, so the previous removal's
    /// DeletedAt makes the re-invite a new email.</summary>
    public static string ReInvite(Guid participantRowId, DateTimeOffset previousDeletedAt) =>
        $"invite:{participantRowId}:{previousDeletedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}";

    public static string Update(Guid eventId, int revision, Guid rowId) =>
        $"update:{eventId}:{revision.ToString(CultureInfo.InvariantCulture)}:{rowId}";

    public static string Cancel(Guid eventId, DateTimeOffset? occurrenceStart, Guid rowId) =>
        $"cancel:{eventId}:{(occurrenceStart is { } start ? start.UtcTicks.ToString(CultureInfo.InvariantCulture) : "series")}:{rowId}";
}
```

- [ ] **Step 2: Rewrite the sender tests (failing)**

Replace `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarNotificationSenderTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
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
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private CalendarNotificationSender BuildSut()
    {
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });
        return new CalendarNotificationSender(_outbox.Object, _employees.Object);
    }

    private void VerifyInApp(Guid userId, string templateCode, Times times) =>
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == userId && p.TemplateCode == templateCode),
            TenantId, It.IsAny<CancellationToken>()), times);

    private void VerifyKeyedEmail(string toEmail, string kind, string key) =>
        _outbox.Verify(x => x.EnqueueKeyedAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == toEmail && p.Kind == kind),
            key, TenantId, It.IsAny<CancellationToken>()), Times.Once);

    private void VerifyUnkeyedEmail(string toEmail, string kind, Times times) =>
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == toEmail && p.Kind == kind),
            TenantId, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task NotifyParticipantsAddedAsync_Keyed_EnqueuesInAppAndAKeyedInvite()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, "Room 4", [new CalendarParticipantRecipient(EmployeeId, "invite:k1")], "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_participant_added", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.Invite, "invite:k1");
        VerifyUnkeyedEmail("ada@example.com", CalendarEmailKinds.Invite, Times.Never());
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_Unkeyed_UsesTheStagedEnqueue()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, "Room 4", [new CalendarParticipantRecipient(EmployeeId)], "Ada Owner", CancellationToken.None);

        VerifyUnkeyedEmail("ada@example.com", CalendarEmailKinds.Invite, Times.Once());
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_SendInviteEmailFalse_SkipsTheEmailButStillNotifiesInApp()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, "Room 4", [new CalendarParticipantRecipient(EmployeeId, "invite:k1")], "Ada Owner",
            CancellationToken.None, sendInviteEmail: false);

        VerifyInApp(UserId, "calendar_event_participant_added", Times.Once());
        _outbox.Verify(x => x.EnqueueKeyedAsync(
            OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(),
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_IncludesMeetingLinkInEmailPayloadWhenProvided()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, null, [new CalendarParticipantRecipient(EmployeeId)], "Ada Owner",
            CancellationToken.None, meetingLink: "https://us05web.zoom.us/j/1");

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.MeetingLink == "https://us05web.zoom.us/j/1"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyGuestsAsync_EnqueuesAKeyedInvitePerGuest_AndNoInAppNotification()
    {
        await BuildSut().NotifyGuestsAsync(
            TenantId, "Vendor sync", Start, null,
            [new CalendarGuestRecipient("vendor@example.com", "invite:g1"), new CalendarGuestRecipient("pm@example.com", "invite:g2")],
            "Ada Owner", null, CancellationToken.None);

        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.Invite, "invite:g1");
        VerifyKeyedEmail("pm@example.com", CalendarEmailKinds.Invite, "invite:g2");
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyMeetingLinkAddedAsync_EnqueuesInviteEmailWithLinkToEachParticipant()
    {
        await BuildSut().NotifyMeetingLinkAddedAsync(
            TenantId, "Standup", Start, null, [EmployeeId], "Ada Owner", "https://teams.microsoft.com/l/1", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com" && p.MeetingLink == "https://teams.microsoft.com/l/1"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyEventUpdatedAsync_InAppToParticipants_UpdatedEmailToParticipantsAndGuests()
    {
        await BuildSut().NotifyEventUpdatedAsync(
            TenantId, "Standup", Start, "Room 4", "https://zoom.us/j/1",
            [new CalendarParticipantRecipient(EmployeeId, "update:p")], [new CalendarGuestRecipient("vendor@example.com", "update:g")],
            "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_updated", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.Updated, "update:p");
        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.Updated, "update:g");
    }

    [Fact]
    public async Task NotifyEventCancelledAsync_InAppToParticipants_CancelledEmailToParticipantsAndGuests()
    {
        await BuildSut().NotifyEventCancelledAsync(
            TenantId, "Standup", Start, null,
            [new CalendarParticipantRecipient(EmployeeId, "cancel:p")], [new CalendarGuestRecipient("vendor@example.com", "cancel:g")],
            "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_cancelled", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.Cancelled, "cancel:p");
        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.Cancelled, "cancel:g");
    }

    [Theory]
    [InlineData(true, "calendar_event_rsvp_accepted")]
    [InlineData(false, "calendar_event_rsvp_rejected")]
    public async Task NotifyRsvpAsync_InAppToTheOrganizer(bool accepted, string templateCode)
    {
        await BuildSut().NotifyRsvpAsync(TenantId, OrganizerUserId, "Standup", "Ada Lovelace", accepted, CancellationToken.None);

        VerifyInApp(OrganizerUserId, templateCode, Times.Once());
    }

    [Fact]
    public async Task NotifyNomineeAsync_InAppToTheNominee()
    {
        await BuildSut().NotifyNomineeAsync(TenantId, EmployeeId, "Standup", "Grace Hopper", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_replacement_nominee", Times.Once());
    }

    [Fact]
    public async Task NotifyParticipantRemovedAsync_InAppAndARemovedEmail()
    {
        await BuildSut().NotifyParticipantRemovedAsync(TenantId, "Standup", Start, null, EmployeeId, "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_participant_removed", Times.Once());
        VerifyUnkeyedEmail("ada@example.com", CalendarEmailKinds.Removed, Times.Once());
    }

    [Fact]
    public async Task NotifyGuestRemovedAsync_ARemovedEmailOnly()
    {
        await BuildSut().NotifyGuestRemovedAsync(TenantId, "Standup", Start, null, "vendor@example.com", "Ada Owner", CancellationToken.None);

        VerifyUnkeyedEmail("vendor@example.com", CalendarEmailKinds.Removed, Times.Once());
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipsAnEmployeeIdThatNoLongerResolves()
    {
        var sut = BuildSut();
        await sut.NotifyEventUpdatedAsync(
            TenantId, "Standup", Start, null, null, [new CalendarParticipantRecipient(Guid.NewGuid(), "update:x")], [],
            "Ada Owner", CancellationToken.None);

        _outbox.VerifyNoOtherCalls();
    }
}
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: compile errors (`CalendarParticipantRecipient` overload mismatch / `NotifyRsvpAsync` does not exist).

- [ ] **Step 3: Rewrite the sender interface and implementation**

Replace `ICalendarNotificationSender.cs` with:

```csharp
namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Stages calendar notifications in the outbox. Unkeyed rows are only persisted by the
/// caller's SaveChangesAsync, so call these BEFORE it (spec §3.1). Keyed emails (a recipient with an
/// EmailKey) are inserted immediately and need the caller's open transaction.</summary>
public interface ICalendarNotificationSender
{
    /// <summary>In-app notification + invite email to each newly-added participant. Pass
    /// <paramref name="sendInviteEmail"/> = false when a Teams/Zoom meeting is about to be attached
    /// in the same create flow; the in-app notification still fires.</summary>
    Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, CancellationToken ct = default,
        string? meetingLink = null, bool sendInviteEmail = true);

    /// <summary>Invite email (with the join link) to each existing participant when a Teams/Zoom
    /// meeting is added after they were invited. No in-app notification.</summary>
    Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default);

    /// <summary>Invite email to external guests (no account, so no in-app notification).</summary>
    Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, string? meetingLink, CancellationToken ct = default);

    /// <summary>In-app to participants + "updated" email to participants and guests.</summary>
    Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default);

    /// <summary>In-app to participants + "cancelled" email to participants and guests.
    /// <paramref name="startDate"/> is the cancelled occurrence's start (or the event's).</summary>
    Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant requested help resolving a conflict.</summary>
    Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant nominated a replacement.</summary>
    Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default);

    /// <summary>In-app notification only, to the event organizer, that a participant accepted or declined.</summary>
    Task NotifyRsvpAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default);

    /// <summary>In-app notification only, to the nominated replacement.</summary>
    Task NotifyNomineeAsync(
        Guid tenantId, Guid nomineeEmployeeId, string eventTitle, string responderName, CancellationToken ct = default);

    /// <summary>In-app + "removed" email to a participant the organizer removed.</summary>
    Task NotifyParticipantRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, Guid employeeId,
        string organizerName, CancellationToken ct = default);

    /// <summary>"Removed" email to a guest the organizer removed.</summary>
    Task NotifyGuestRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string guestEmail,
        string organizerName, CancellationToken ct = default);
}
```

Replace `CalendarNotificationSender.cs` with:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

public sealed class CalendarNotificationSender(IOutboxWriter outboxWriter, IEmployeeRepository employees) : ICalendarNotificationSender
{
    public async Task NotifyParticipantsAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, CancellationToken ct = default,
        string? meetingLink = null, bool sendInviteEmail = true)
    {
        foreach (var recipient in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, recipient.EmployeeId, ct);
            if (employee is null) continue;

            await EnqueueInAppAsync(tenantId, employee.UserId, "calendar_event_participant_added",
                new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle, ["eventDate"] = startDate.ToString("u") }, ct);

            if (!sendInviteEmail) continue;

            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location, organizerName, meetingLink),
                recipient.EmailKey, ct);
        }
    }

    public async Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<Guid> employeeIds, string organizerName, string meetingLink, CancellationToken ct = default)
    {
        foreach (var employeeId in employeeIds)
        {
            var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is null) continue;

            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location, organizerName, meetingLink),
                emailKey: null, ct);
        }
    }

    public async Task NotifyGuestsAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, string? meetingLink, CancellationToken ct = default)
    {
        foreach (var guest in guests)
        {
            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, guest.Email, GuestName(guest.Email), eventTitle, startDate, location, organizerName, meetingLink),
                guest.EmailKey, ct);
        }
    }

    public Task NotifyEventUpdatedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string? meetingLink,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default)
        => NotifyChangeAsync(tenantId, "calendar_event_updated", CalendarEmailKinds.Updated, eventTitle, startDate, location,
            meetingLink, participants, guests, organizerName, ct);

    public Task NotifyEventCancelledAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default)
        => NotifyChangeAsync(tenantId, "calendar_event_cancelled", CalendarEmailKinds.Cancelled, eventTitle, startDate, location,
            meetingLink: null, participants, guests, organizerName, ct);

    public Task NotifyResolutionRequestedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string reason, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, "calendar_event_resolution_requested",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle, ["reason"] = reason }, ct);

    public Task NotifyReplacementNominatedAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, string nomineeName, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, "calendar_event_replacement_nominated",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle, ["nomineeName"] = nomineeName }, ct);

    public Task NotifyRsvpAsync(
        Guid tenantId, Guid organizerUserId, string eventTitle, string responderName, bool accepted, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, accepted ? "calendar_event_rsvp_accepted" : "calendar_event_rsvp_rejected",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle }, ct);

    public async Task NotifyNomineeAsync(
        Guid tenantId, Guid nomineeEmployeeId, string eventTitle, string responderName, CancellationToken ct = default)
    {
        var nominee = await employees.GetByIdAsync(tenantId, nomineeEmployeeId, ct);
        if (nominee is null) return;

        await EnqueueInAppAsync(tenantId, nominee.UserId, "calendar_event_replacement_nominee",
            new Dictionary<string, string> { ["responderName"] = responderName, ["eventTitle"] = eventTitle }, ct);
    }

    public async Task NotifyParticipantRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, Guid employeeId,
        string organizerName, CancellationToken ct = default)
    {
        var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null) return;

        await EnqueueInAppAsync(tenantId, employee.UserId, "calendar_event_participant_removed",
            new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle }, ct);
        await EnqueueEmailAsync(tenantId,
            new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location,
                organizerName, null, CalendarEmailKinds.Removed),
            emailKey: null, ct);
    }

    public Task NotifyGuestRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, string guestEmail,
        string organizerName, CancellationToken ct = default)
        => EnqueueEmailAsync(tenantId,
            new CalendarEventInviteEmailPayload(tenantId, guestEmail, GuestName(guestEmail), eventTitle, startDate, location,
                organizerName, null, CalendarEmailKinds.Removed),
            emailKey: null, ct);

    private async Task NotifyChangeAsync(
        Guid tenantId, string templateCode, string emailKind, string eventTitle, DateTimeOffset startDate, string? location,
        string? meetingLink, IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct)
    {
        foreach (var recipient in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, recipient.EmployeeId, ct);
            if (employee is null) continue;

            await EnqueueInAppAsync(tenantId, employee.UserId, templateCode,
                new Dictionary<string, string> { ["organizerName"] = organizerName, ["eventTitle"] = eventTitle }, ct);
            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location,
                    organizerName, meetingLink, emailKind),
                recipient.EmailKey, ct);
        }

        foreach (var guest in guests)
        {
            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, guest.Email, GuestName(guest.Email), eventTitle, startDate, location,
                    organizerName, meetingLink, emailKind),
                guest.EmailKey, ct);
        }
    }

    private Task EnqueueInAppAsync(Guid tenantId, Guid userId, string templateCode, Dictionary<string, string> placeholders, CancellationToken ct)
        => outboxWriter.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            new WorkNotificationPayload(tenantId, userId, templateCode, placeholders, "calendar_event", null),
            tenantId, ct);

    private async Task EnqueueEmailAsync(Guid tenantId, CalendarEventInviteEmailPayload payload, string? emailKey, CancellationToken ct)
    {
        if (emailKey is null)
            await outboxWriter.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail, payload, tenantId, ct);
        else
            await outboxWriter.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail, payload, emailKey, tenantId, ct);
    }

    private static string FullName(Employee employee) => $"{employee.FirstName} {employee.LastName}";

    private static string GuestName(string email) => email.Split('@')[0];
}
```

- [ ] **Step 4: Seed the four new in-app templates**

In `NotificationTemplateSeeder.cs`, after the `calendar_event_replacement_nominated` entry add:

```csharp
            ,
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_rsvp_accepted",
                InAppTitleTemplate = "Invitation accepted",
                InAppBodyTemplate = "{{responderName}} accepted \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_rsvp_rejected",
                InAppTitleTemplate = "Invitation declined",
                InAppBodyTemplate = "{{responderName}} declined \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_participant_removed",
                InAppTitleTemplate = "Removed from an event",
                InAppBodyTemplate = "{{organizerName}} removed you from \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_replacement_nominee",
                InAppTitleTemplate = "You were nominated",
                InAppBodyTemplate = "{{responderName}} nominated you to attend \"{{eventTitle}}\" in their place."
            }
```

In `NotificationTemplateSeederTests`, in the expected-codes list replace `"calendar_event_replacement_nominated"` with:

```csharp
            "calendar_event_replacement_nominated",
            "calendar_event_rsvp_accepted",
            "calendar_event_rsvp_rejected",
            "calendar_event_participant_removed",
            "calendar_event_replacement_nominee"
```

- [ ] **Step 5: Update the call sites (invite keys for creation; behaviour-preserving elsewhere)**

`CreateCalendarEventCommandHandler.cs`: replace the participant notify call with:

```csharp
                await notifications.NotifyParticipantsAddedAsync(
                    tenantId, calendarEvent.Title, calendarEvent.StartDate, calendarEvent.Location,
                    participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Invite(p.Id))).ToList(),
                    organizerName, innerCt, sendInviteEmail: !hasPendingMeeting);
```

and replace the guest block's `AddGuestsAsync(...)` plus `NotifyGuestsAsync(...)` with:

```csharp
                var guestRows = guestEmails.Select(email => new CalendarEventGuest
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = calendarEvent.Id, Email = email
                }).ToList();
                await events.AddGuestsAsync(guestRows, innerCt);

                // Guests have no account, so there is no in-app fallback the way participants get one
                // above - when a meeting is about to be attached, skip inviting them now entirely and
                // let NotifyMeetingLinkAddedAsync send their one invite once the join link exists.
                if (!hasPendingMeeting)
                {
                    var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                    var guestOrganizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
                    await notifications.NotifyGuestsAsync(
                        tenantId, calendarEvent.Title, calendarEvent.StartDate, calendarEvent.Location,
                        guestRows.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Invite(g.Id))).ToList(),
                        guestOrganizerName, calendarEvent.MeetingLink, innerCt);
                }
```

`AddCalendarEventParticipantsCommandHandler.cs`: replace from `var removedRows = …` through the `NotifyParticipantsAddedAsync(…);` call with:

```csharp
                var removedRows = await events.GetRemovedParticipantsAsync(tenantId, existing.Id, newEmployeeIds, innerCt);
                var recipients = new List<CalendarParticipantRecipient>();
                foreach (var row in removedRows)
                {
                    // Reviving reuses the row, so key the re-invite on the removal it undoes.
                    recipients.Add(new CalendarParticipantRecipient(
                        row.EmployeeId, CalendarEmailKeys.ReInvite(row.Id, row.DeletedAt ?? row.CreatedAt)));
                    row.IsDeleted = false;
                    row.DeletedAt = null;
                    row.ResponseStatus = CalendarEventParticipantStatuses.Pending;
                    row.ResponseReason = null;
                }
                currentParticipants.AddRange(removedRows);

                var revivedEmployeeIds = removedRows.Select(r => r.EmployeeId).ToHashSet();
                var newParticipants = newEmployeeIds.Where(id => !revivedEmployeeIds.Contains(id)).Select(employeeId => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EventId = existing.Id, EmployeeId = employeeId,
                    ResponseStatus = CalendarEventParticipantStatuses.Pending
                }).ToList();
                if (newParticipants.Count > 0)
                    await events.AddParticipantsAsync(newParticipants, innerCt);
                currentParticipants.AddRange(newParticipants);
                recipients.AddRange(newParticipants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Invite(p.Id))));

                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyParticipantsAddedAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    recipients, organizerName, innerCt, existing.MeetingLink);
```

`AddCalendarEventGuestsCommandHandler.cs`: replace `newEmails, organizerName, existing.MeetingLink, innerCt);` with:

```csharp
                    newGuests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Invite(g.Id))).ToList(),
                    organizerName, existing.MeetingLink, innerCt);
```

`CreateEventMeetingCommandHandler.cs:120-122`: replace `guestEmails, organizerName, joinUrl, innerCt);` with `guestEmails.Select(email => new CalendarGuestRecipient(email)).ToList(), organizerName, joinUrl, innerCt);`. These link emails stay unkeyed until plan 0C.

`UpdateCalendarEventCommandHandler.cs`: replace `await notifications.NotifyEventUpdatedAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, innerCt);` with:

```csharp
                await notifications.NotifyEventUpdatedAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location, existing.MeetingLink,
                    participantEmployeeIds.Select(id => new CalendarParticipantRecipient(id)).ToList(), [], organizerName, innerCt);
```

`EditRecurringOccurrenceCommandHandler.cs` (`EditAllEventsAsync`): replace `await notifications.NotifyEventUpdatedAsync(master.TenantId, master.Title, participantEmployeeIds, organizerName, ct);` with:

```csharp
            await notifications.NotifyEventUpdatedAsync(
                master.TenantId, master.Title, master.StartDate, master.Location, master.MeetingLink,
                participantEmployeeIds.Select(id => new CalendarParticipantRecipient(id)).ToList(), [], organizerName, ct);
```

`CancelRecurringOccurrenceCommandHandler.cs`: replace `await notifications.NotifyEventCancelledAsync(tenantId, master.Title, participantEmployeeIds, organizerName, ct);` with:

```csharp
            await notifications.NotifyEventCancelledAsync(
                tenantId, master.Title, request.OriginalStart, master.Location,
                participantEmployeeIds.Select(id => new CalendarParticipantRecipient(id)).ToList(), [], organizerName, ct);
```

`DeleteCalendarEventCommandHandler.cs`: replace `await notifications.NotifyEventCancelledAsync(tenantId, existing.Title, participantEmployeeIds, organizerName, ct);` with:

```csharp
            await notifications.NotifyEventCancelledAsync(
                tenantId, existing.Title, existing.StartDate, existing.Location,
                participantEmployeeIds.Select(id => new CalendarParticipantRecipient(id)).ToList(), [], organizerName, ct);
```

(Tasks 4–6 replace the update and cancel call sites with keyed, guest-inclusive, correctly-ordered versions. This step only keeps them compiling with today's behaviour.)

- [ ] **Step 6: Update the handler unit tests for the new signatures, and add the invite-key tests**

Make these exact replacements:

| File (`tests/ONEVO.Tests.Unit/Features/Calendar/`) | Replace | With |
|---|---|---|
| `CreateCalendarEventCommandHandlerTests.cs` (all occurrences) | `It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId)` | `It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId)` |
| same | `It.IsAny<IReadOnlyList<Guid>>()` | `It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>()` |
| same | `It.IsAny<IReadOnlyList<string>>()` | `It.IsAny<IReadOnlyList<CalendarGuestRecipient>>()` |
| same | `It.Is<IReadOnlyList<string>>(e => e.SequenceEqual(new[] { "vendor@example.com", "pm@example.com" }))` | `It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Select(x => x.Email).SequenceEqual(new[] { "vendor@example.com", "pm@example.com" }))` |
| `AddCalendarEventParticipantsCommandHandlerTests.cs` | `It.Is<IReadOnlyList<Guid>>(ids => ids.Single() == NewEmployeeId)` | `It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmployeeId == NewEmployeeId)` |
| same | `It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2 && ids.Contains(NewEmployeeId) && ids.Contains(brandNewEmployeeId))` | `It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 2 && r.Any(x => x.EmployeeId == NewEmployeeId) && r.Any(x => x.EmployeeId == brandNewEmployeeId))` |
| same | `It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()` | `It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()` |
| `AddCalendarEventGuestsCommandHandlerTests.cs` | `It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "guest@example.com", "other@example.com" }))` | `It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Select(x => x.Email).SequenceEqual(new[] { "guest@example.com", "other@example.com" }))` |
| same | `It.IsAny<IReadOnlyList<string>>()` | `It.IsAny<IReadOnlyList<CalendarGuestRecipient>>()` |
| `CreateEventMeetingCommandHandlerTests.cs` (the `NotifyGuestsAsync` verify only) | `It.Is<IReadOnlyList<string>>(emails => emails.Single() == "vendor@example.com")` | `It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().Email == "vendor@example.com" && g.Single().EmailKey == null)` |
| `UpdateCalendarEventCommandHandlerTests.cs` | `TenantId, "New Title", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),` | `TenantId, "New Title", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(), It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),` |
| `EditRecurringOccurrenceCommandHandlerTests.cs` | the same string as the Update row | the same replacement as the Update row |
| `CancelRecurringOccurrenceCommandHandlerTests.cs` | `TenantId, "Standup", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),` | `TenantId, "Standup", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),` |
| `DeleteCalendarEventCommandHandlerTests.cs` | `TenantId, "Event", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),` | `TenantId, "Event", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),` |

Add `using ONEVO.Application.Features.Calendar.Services;` to any of these files that lacks it.

Append to `AddCalendarEventParticipantsCommandHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_NewParticipant_GetsAnInviteKeyedOnItsNewRow()
    {
        var evt = MakeEvent();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [] });
        CalendarEventParticipant? added = null;
        _events.Setup(e => e.AddParticipantsAsync(It.IsAny<IReadOnlyList<CalendarEventParticipant>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<CalendarEventParticipant>, CancellationToken>((rows, _) => added = rows.Single())
            .Returns(Task.CompletedTask);

        await BuildSut().Handle(new AddCalendarEventParticipantsCommand(EventId, [NewEmployeeId]), CancellationToken.None);

        _notifications.Verify(n => n.NotifyParticipantsAddedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"invite:{added!.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>(), evt.MeetingLink), Times.Once);
    }

    [Fact]
    public async Task Handle_PreviouslyRemovedEmployee_GetsAReInviteKeyedOnTheRemoval()
    {
        var sut = BuildSut();
        var evt = MakeEvent();
        var deletedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var removedRow = new CalendarEventParticipant
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = NewEmployeeId,
            ResponseStatus = CalendarEventParticipantStatuses.Rejected, IsDeleted = true, DeletedAt = deletedAt
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [] });
        _events.Setup(e => e.GetRemovedParticipantsAsync(TenantId, EventId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([removedRow]);

        await sut.Handle(new AddCalendarEventParticipantsCommand(EventId, [NewEmployeeId]), CancellationToken.None);

        _notifications.Verify(n => n.NotifyParticipantsAddedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"invite:{removedRow.Id}:{deletedAt.UtcTicks}"),
            "Ada Owner", It.IsAny<CancellationToken>(), evt.MeetingLink), Times.Once);
    }
```

- [ ] **Step 7: Run the calendar, seeder and email tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~Features.Calendar|FullyQualifiedName~NotificationTemplateSeederTests"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs tests/ONEVO.Tests.Unit
git commit -m "feat(calendar-notifications): recipient-based sender with keyed invites, update/cancel emails and RSVP/removal notices

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: P0-1a — event update notifies before saving, with guests and keyed "updated" emails

**Files:**
- Modify: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs`
- Create (generated): `src/ONEVO.Infrastructure/Migrations/<ts>_AddCalendarEventRevision.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Produces: `CalendarEvent.Revision` (`int`, starts at 0, incremented by every update of that event row). Tasks 5 and 9 use it.

- [ ] **Step 1: Add the revision**

In `CalendarEvent`, after `public bool IsRecurrenceCancelled { get; set; }` add:

```csharp
    /// <summary>Incremented by every organizer edit of this row. Part of the "updated" email key
    /// (spec X-1), so each genuine update emails once and a retry of the same update does not.</summary>
    public int Revision { get; set; }
```

Generate the migration:

```bash
ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none" dotnet ef migrations add AddCalendarEventRevision --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```
Expected: `Up` has `AddColumn<int>(name: "revision", table: "calendar_events", nullable: false, defaultValue: 0)`.

- [ ] **Step 2: Write the failing tests**

In `UpdateCalendarEventCommandHandlerTests.BuildSut`, before `return`, add:

```csharp
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
```

Append:

```csharp
    [Fact]
    public async Task Handle_NotifiesBeforeTheSave_SoTheOutboxRowsCommitWithTheChange()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() }]
            });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
    }

    [Fact]
    public async Task Handle_IncrementsTheRevision_AndKeysParticipantAndGuestEmailsOnIt()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30), Revision = 4 };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var participantRow = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        var guestRow = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participantRow] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guestRow] });

        await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, "Room 2", "https://zoom.us/j/9", null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.Equal(5, existing.Revision);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(
            TenantId, "New Title", Start, "Room 2", "https://zoom.us/j/9",
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"update:{EventId}:5:{participantRow.Id}"),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().Email == "vendor@example.com" && g.Single().EmailKey == $"update:{EventId}:5:{guestRow.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoParticipantsOrGuests_NotifiesNoOne_AndStillSaves()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Old", StartDate = Start, EndDate = Start.AddMinutes(30) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await sut.Handle(
            new UpdateCalendarEventCommand(EventId, "New Title", null, Start, Start.AddHours(1), false, null, null, null, CalendarRecurrences.None),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.VerifyNoOtherCalls();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~UpdateCalendarEventCommandHandlerTests"`
Expected: the 3 new tests FAIL (`["save","notify"]`; revision 4; keys null).

- [ ] **Step 3: Implement**

In `UpdateCalendarEventCommandHandler`, add `using ONEVO.Domain.Features.Calendar.Entities;` and replace the `ExecuteInTransactionAsync` lambda body from `events.Update(existing);` through the closing `}` of the `if (participantEmployeeIds.Count > 0)` block with:

```csharp
            existing.Revision++;
            events.Update(existing);

            // Spec §3.1: stage every notification BEFORE the single SaveChangesAsync, so the change
            // and its notifications commit together (the sender only stages outbox rows).
            await NotifyUpdatedAsync(tenantId, existing, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
```

and add this method to the class:

```csharp
    private async Task NotifyUpdatedAsync(Guid tenantId, CalendarEvent updated, CancellationToken ct)
    {
        var participants = (await events.GetParticipantsForEventsAsync(tenantId, [updated.Id], ct)).GetValueOrDefault(updated.Id) ?? [];
        var guests = (await events.GetGuestsForEventsAsync(tenantId, [updated.Id], ct)).GetValueOrDefault(updated.Id) ?? [];
        if (participants.Count == 0 && guests.Count == 0)
            return;

        var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
        await notifications.NotifyEventUpdatedAsync(
            tenantId, updated.Title, updated.StartDate, updated.Location, updated.MeetingLink,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Update(updated.Id, updated.Revision, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Update(updated.Id, updated.Revision, g.Id))).ToList(),
            organizerName, ct);
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~UpdateCalendarEventCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs src/ONEVO.Infrastructure/Migrations src/ONEVO.Application/Features/Calendar/Commands/UpdateCalendarEvent tests/ONEVO.Tests.Unit/Features/Calendar/UpdateCalendarEventCommandHandlerTests.cs
git commit -m "fix(calendar): event updates notify before saving and email participants and guests once per revision

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: P0-1b and P0-3 — recurring edits notify every scope, and "this and following" keeps its attendees

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence/EditRecurringOccurrenceCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/EditRecurringOccurrenceCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `CalendarEvent.Revision` (Task 4), `CalendarEmailKeys.Update`, recipient records (Task 3).

- [ ] **Step 1: Write the failing tests (P0-3 first, per the spec: "write a failing test first; fix only if it fails")**

In `EditRecurringOccurrenceCommandHandlerTests.BuildSut`, before `return`, add:

```csharp
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
```

Append (the file's existing helpers give `TenantId`, `UserId`, `MasterId`; if the master-builder helper in the file has another name, use it. These tests build their own master so they don't depend on it):

```csharp
    private static CalendarEvent RecurringMaster(Guid masterId) => new()
    {
        Id = masterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        StartDate = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), EndDate = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero),
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY;BYDAY=TH"
    };

    private void SetupAttendees(Guid masterId, CalendarEventParticipant participant, CalendarEventGuest guest)
    {
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(masterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [masterId] = [participant] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(masterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [masterId] = [guest] });
    }

    private static EditRecurringOccurrenceCommand Command(Guid masterId, RecurrenceEditScope scope, DateTimeOffset originalStart) => new(
        masterId, scope, originalStart, "New Title", null, originalStart, originalStart.AddMinutes(30), false, null, null, null);

    [Fact]
    public async Task Handle_ThisAndFollowing_CopiesParticipantsAndGuestsOntoTheNewSeries()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid(), ResponseStatus = CalendarEventParticipantStatuses.Accepted };
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" };
        SetupAttendees(masterId, participant, guest);
        CalendarEvent? newMaster = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => newMaster = e).Returns(Task.CompletedTask);

        var result = await sut.Handle(Command(masterId, RecurrenceEditScope.ThisAndFollowing, master.StartDate.AddDays(14)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddParticipantsAsync(
            It.Is<IReadOnlyList<CalendarEventParticipant>>(rows => rows.Count == 1 && rows[0].EventId == newMaster!.Id
                && rows[0].EmployeeId == participant.EmployeeId && rows[0].Id != participant.Id
                && rows[0].ResponseStatus == CalendarEventParticipantStatuses.Accepted),
            It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.AddGuestsAsync(
            It.Is<IReadOnlyList<CalendarEventGuest>>(rows => rows.Count == 1 && rows[0].EventId == newMaster!.Id
                && rows[0].Email == "vendor@example.com" && rows[0].Id != guest.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_NotifiesTheCopiedAttendeesBeforeSaving()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        SetupAttendees(masterId,
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid() },
            new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(Command(masterId, RecurrenceEditScope.ThisAndFollowing, master.StartDate.AddDays(14)), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NotifiesTheSeriesAttendees_KeyedOnTheOccurrenceRow()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid() };
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" };
        SetupAttendees(masterId, participant, guest);
        var originalStart = master.StartDate.AddDays(7);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, masterId, originalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        CalendarEvent? child = null;
        _events.Setup(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => child = e).Returns(Task.CompletedTask);
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(Command(masterId, RecurrenceEditScope.ThisEventOnly, originalStart), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
        _notifications.Verify(x => x.NotifyEventUpdatedAsync(
            TenantId, "New Title", originalStart, It.IsAny<string?>(), It.IsAny<string?>(),
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"update:{child!.Id}:1:{participant.Id}"),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"update:{child!.Id}:1:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AllEvents_NotifiesBeforeSaving()
    {
        var sut = BuildSut();
        var masterId = Guid.NewGuid();
        var master = RecurringMaster(masterId);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, masterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        SetupAttendees(masterId,
            new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, EmployeeId = Guid.NewGuid() },
            new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = masterId, Email = "vendor@example.com" });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventUpdatedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(Command(masterId, RecurrenceEditScope.AllEvents, master.StartDate), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
        Assert.Equal(1, master.Revision);
    }
```

Check the `EditRecurringOccurrenceCommand` constructor's parameter order in `EditRecurringOccurrenceCommand.cs` before running. If it differs from `(MasterId, Scope, OriginalStart, Title, Description, StartDate, EndDate, IsAllDay, Location, MeetingLink, Color)`, reorder the arguments in `Command(...)` to match. Only the helper changes.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~EditRecurringOccurrenceCommandHandlerTests"`
Expected: the 4 new tests FAIL. `AddParticipantsAsync` is never called (P0-3 confirmed), and the call order is `["save","notify"]` or nothing is notified.

- [ ] **Step 2: Implement**

Add `using ONEVO.Domain.Features.Calendar.Entities;` if missing. Replace `EditAllEventsAsync`, `EditThisEventOnlyAsync` and `EditThisAndFollowingAsync` with:

```csharp
    private Task<Result<CalendarEventItem>> EditAllEventsAsync(
        CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
        => unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            ApplyFields(master, request);
            master.Revision++;
            events.Update(master);

            await NotifyAttendeesOfAsync(master.TenantId, attendeesOf: master.Id, changed: master, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(master, isOccurrence: false, masterId: null, originalStart: null));
        }, ct);

    private Task<Result<CalendarEventItem>> EditThisEventOnlyAsync(
        Guid tenantId, CalendarEvent master, EditRecurringOccurrenceCommand request, CancellationToken ct)
        => unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, innerCt);
            if (child is null)
            {
                child = new CalendarEvent
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, RecurrenceParentId = master.Id,
                    RecurrenceOriginalStart = request.OriginalStart, Recurrence = CalendarRecurrences.None,
                    SourceType = master.SourceType, Timezone = master.Timezone
                };
                ApplyFields(child, request);
                child.Revision++;
                await events.AddAsync(child, innerCt);
            }
            else
            {
                child.IsRecurrenceCancelled = false;
                ApplyFields(child, request);
                child.Revision++;
                events.Update(child);
            }

            // The occurrence has no attendee rows of its own: the series' attendees are its attendees.
            await NotifyAttendeesOfAsync(tenantId, attendeesOf: master.Id, changed: child, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(child, isOccurrence: true, masterId: master.Id, originalStart: request.OriginalStart));
        }, ct);

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
                Recurrence = master.Recurrence, RecurrenceRule = originalRule, SourceType = master.SourceType,
                Timezone = master.Timezone, Revision = 1
            };
            ApplyFields(newMaster, request);
            await events.AddAsync(newMaster, innerCt);

            // P0-3: the new series keeps the series' participants (with their responses) and guests.
            var (participants, guests) = await CopyAttendeesAsync(tenantId, master.Id, newMaster.Id, innerCt);

            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, innerCt);
            foreach (var child in children.Where(c => c.RecurrenceOriginalStart >= request.OriginalStart))
            {
                var tracked = await events.GetTrackedByIdForTenantAsync(tenantId, child.Id, innerCt);
                if (tracked is null) continue;
                tracked.RecurrenceParentId = newMaster.Id;
                events.Update(tracked);
            }

            await NotifyRecipientsAsync(tenantId, newMaster, participants, guests, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CalendarEventItem>.Success(ToItem(newMaster, isOccurrence: true, masterId: newMaster.Id, originalStart: request.StartDate));
        }, ct);
    }

    private async Task<(IReadOnlyList<CalendarEventParticipant> Participants, IReadOnlyList<CalendarEventGuest> Guests)> CopyAttendeesAsync(
        Guid tenantId, Guid fromEventId, Guid toEventId, CancellationToken ct)
    {
        var participants = ((await events.GetParticipantsForEventsAsync(tenantId, [fromEventId], ct)).GetValueOrDefault(fromEventId) ?? [])
            .Select(p => new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventId = toEventId, EmployeeId = p.EmployeeId,
                ResponseStatus = p.ResponseStatus, ResponseReason = p.ResponseReason
            })
            .ToList();
        if (participants.Count > 0)
            await events.AddParticipantsAsync(participants, ct);

        var guests = ((await events.GetGuestsForEventsAsync(tenantId, [fromEventId], ct)).GetValueOrDefault(fromEventId) ?? [])
            .Select(g => new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = tenantId, EventId = toEventId, Email = g.Email })
            .ToList();
        if (guests.Count > 0)
            await events.AddGuestsAsync(guests, ct);

        return (participants, guests);
    }

    private async Task NotifyAttendeesOfAsync(Guid tenantId, Guid attendeesOf, CalendarEvent changed, CancellationToken ct)
    {
        var participants = (await events.GetParticipantsForEventsAsync(tenantId, [attendeesOf], ct)).GetValueOrDefault(attendeesOf) ?? [];
        var guests = (await events.GetGuestsForEventsAsync(tenantId, [attendeesOf], ct)).GetValueOrDefault(attendeesOf) ?? [];
        await NotifyRecipientsAsync(tenantId, changed, participants, guests, ct);
    }

    private async Task NotifyRecipientsAsync(
        Guid tenantId, CalendarEvent changed, IReadOnlyList<CalendarEventParticipant> participants,
        IReadOnlyList<CalendarEventGuest> guests, CancellationToken ct)
    {
        if (participants.Count == 0 && guests.Count == 0)
            return;

        var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
        await notifications.NotifyEventUpdatedAsync(
            tenantId, changed.Title, changed.StartDate, changed.Location, changed.MeetingLink,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Update(changed.Id, changed.Revision, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Update(changed.Id, changed.Revision, g.Id))).ToList(),
            organizerName, ct);
    }
```

The existing `ThisEventOnly` and `AllEvents` unit tests call `SaveChangesAsync` through the transaction setup already in `BuildSut`, so they keep working.

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~EditRecurringOccurrenceCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/EditRecurringOccurrence tests/ONEVO.Tests.Unit/Features/Calendar/EditRecurringOccurrenceCommandHandlerTests.cs
git commit -m "fix(calendar): recurring edits notify every scope before saving; this-and-following keeps attendees

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: P0-1c and P1-3 — cancelling an occurrence and deleting an event notify participants and guests before saving

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence/CancelRecurringOccurrenceCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/CancelRecurringOccurrenceCommandHandlerTests.cs`, `DeleteCalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `CalendarEmailKeys.Cancel`, recipient records (Task 3).

- [ ] **Step 1: Write the failing tests**

In both test classes' `BuildSut`, before `return`, add:

```csharp
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>, CancellationToken>((op, ct) => op(ct));
```

Append to `CancelRecurringOccurrenceCommandHandlerTests` (use the file's existing master-id and command conventions: `MasterId`, `CancelRecurringOccurrenceCommand(MasterId, originalStart)`. Adjust names only if the file uses different ones):

```csharp
    [Fact]
    public async Task Handle_NotifiesParticipantsAndGuestsBeforeSaving_KeyedOnTheOccurrence()
    {
        var sut = BuildSut();
        var master = new CalendarEvent
        {
            Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup", Location = "Room 4",
            StartDate = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), EndDate = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero),
            Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY"
        };
        var originalStart = master.StartDate.AddDays(7);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, originalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid() };
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, Email = "vendor@example.com" };
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [MasterId] = [participant] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [MasterId] = [guest] });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventCancelledAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, originalStart), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
        _notifications.Verify(x => x.NotifyEventCancelledAsync(
            TenantId, "Standup", originalStart, "Room 4",
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"cancel:{MasterId}:{originalStart.UtcTicks}:{participant.Id}"),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"cancel:{MasterId}:{originalStart.UtcTicks}:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }
```

Append to `DeleteCalendarEventCommandHandlerTests` (the file uses `EventId`):

```csharp
    [Fact]
    public async Task Handle_EmailsGuestsToo_LoadedBeforeTheEventIsRemoved_AndNotifiesBeforeSaving()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event", StartDate = DateTimeOffset.UtcNow.AddDays(1), EndDate = DateTimeOffset.UtcNow.AddDays(1).AddHours(1) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guest] });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventCancelledAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _events.Setup(x => x.Remove(existing)).Callback(() => calls.Add("remove"));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.Equal(new[] { "notify", "remove", "save" }, calls);
        _notifications.Verify(x => x.NotifyEventCancelledAsync(
            TenantId, "Event", existing.StartDate, It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"cancel:{EventId}:series:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }
```

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~CancelRecurringOccurrenceCommandHandlerTests|FullyQualifiedName~DeleteCalendarEventCommandHandlerTests"`
Expected: the 2 new tests FAIL.

- [ ] **Step 2: Implement — cancel occurrence**

Replace `CancelRecurringOccurrenceCommandHandler.Handle` from `var child = await events.GetTrackedChildByOriginalStartAsync(...)` to the end of the method with:

```csharp
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var child = await events.GetTrackedChildByOriginalStartAsync(tenantId, master.Id, request.OriginalStart, innerCt);
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
                await events.AddAsync(child, innerCt);
            }
            else
            {
                child.IsRecurrenceCancelled = true;
                events.Update(child);
            }

            // Spec §3.1: stage before the save. Keyed on the occurrence, so cancelling it twice
            // can't email anyone twice.
            var participants = (await events.GetParticipantsForEventsAsync(tenantId, [master.Id], innerCt)).GetValueOrDefault(master.Id) ?? [];
            var guests = (await events.GetGuestsForEventsAsync(tenantId, [master.Id], innerCt)).GetValueOrDefault(master.Id) ?? [];
            if (participants.Count > 0 || guests.Count > 0)
            {
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyEventCancelledAsync(
                    tenantId, master.Title, request.OriginalStart, master.Location,
                    participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Cancel(master.Id, request.OriginalStart, p.Id))).ToList(),
                    guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Cancel(master.Id, request.OriginalStart, g.Id))).ToList(),
                    organizerName, innerCt);
            }

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
```

- [ ] **Step 3: Implement — delete**

In `DeleteCalendarEventCommandHandler.Handle`, replace everything after the `Forbidden` check with:

```csharp
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Plan 0C (X-2) replaces this remote cancel with an after-commit outbox message.
            var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, innerCt);
            if (meeting is not null && meeting.Status == CalendarEventMeetingStatuses.Active)
            {
                var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, innerCt);
                if (connection is not null)
                {
                    var isZoom = meeting.Provider == CalendarEventMeetingProviders.Zoom;
                    var oauthProvider = isZoom ? "zoom" : "microsoft";
                    var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, innerCt);
                    if (accessToken is not null)
                    {
                        if (isZoom)
                            await zoomClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, innerCt);
                        else
                            await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, innerCt);
                    }
                }
            }

            // Load participants AND guests before the event is removed, and stage before the save.
            var participants = (await events.GetParticipantsForEventsAsync(tenantId, [existing.Id], innerCt)).GetValueOrDefault(existing.Id) ?? [];
            var guests = (await events.GetGuestsForEventsAsync(tenantId, [existing.Id], innerCt)).GetValueOrDefault(existing.Id) ?? [];
            if (participants.Count > 0 || guests.Count > 0)
            {
                var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
                await notifications.NotifyEventCancelledAsync(
                    tenantId, existing.Title, existing.StartDate, existing.Location,
                    participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Cancel(existing.Id, null, p.Id))).ToList(),
                    guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Cancel(existing.Id, null, g.Id))).ToList(),
                    organizerName, innerCt);
            }

            events.Remove(existing);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
```

Add `using ONEVO.Application.Features.Calendar.Services;` and `using ONEVO.Domain.Features.Calendar.Entities;` to both handlers if missing.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~CancelRecurringOccurrenceCommandHandlerTests|FullyQualifiedName~DeleteCalendarEventCommandHandlerTests"`
Expected: all pass (existing tests, including the remote-cancel ones, still pass through the transaction setup).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/CancelRecurringOccurrence src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent tests/ONEVO.Tests.Unit/Features/Calendar/CancelRecurringOccurrenceCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/DeleteCalendarEventCommandHandlerTests.cs
git commit -m "fix(calendar): cancel and delete notify participants and guests before saving, once per occurrence

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: P1-2 and P1-4 (nominee) — accepted and declined invitations reach the organizer; the nominee is told

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent/RespondToCalendarEventCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Append (the file defines `TenantId`, `EventId`, `EmployeeId`, `OrganizerUserId`, a `BuildSut()` and a participant setup; reuse them. These tests set up what they need explicitly):

```csharp
    [Theory]
    [InlineData("Accepted", true)]
    [InlineData("Rejected", false)]
    public async Task Handle_AcceptedOrRejected_NotifiesTheOrganizerBeforeSaving(string response, bool accepted)
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId };
        _events.Setup(e => e.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);
        _events.Setup(e => e.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, Title = "Payroll review", CreatedById = OrganizerUserId });
        var calls = new List<string>();
        _notifications.Setup(n => n.NotifyRsvpAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, response), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(n => n.NotifyRsvpAsync(TenantId, OrganizerUserId, "Payroll review", "Ada Lovelace", accepted, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "notify", "save" }, calls);
    }

    [Fact]
    public async Task Handle_ReplacementNominated_AlsoTellsTheNominee()
    {
        var sut = BuildSut();
        var nomineeId = Guid.NewGuid();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId };
        _events.Setup(e => e.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [participant, new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = nomineeId }]
            });
        _employees.Setup(e => e.GetByIdAsync(TenantId, nomineeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = nomineeId, TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "Grace", LastName = "Hopper" });
        _events.Setup(e => e.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, Title = "Payroll review", CreatedById = OrganizerUserId });

        var result = await sut.Handle(
            new RespondToCalendarEventCommand(EventId, "ReplacementNominated", null, nomineeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(n => n.NotifyNomineeAsync(TenantId, nomineeId, "Payroll review", "Ada Lovelace", It.IsAny<CancellationToken>()), Times.Once);
    }
```

Match the `RespondToCalendarEventCommand` constructor to the one used by the file's existing `ReplacementNominated` test (`(EventId, status, reason, nomineeEmployeeId)`). Match the caller-employee setup to the one `BuildSut` already arranges, so the responder name is "Ada Lovelace".

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~RespondToCalendarEventCommandHandlerTests"`
Expected: the 3 new cases FAIL.

- [ ] **Step 2: Implement**

Replace the block from `// Enqueue the organizer notification BEFORE …` through its closing `}` with:

```csharp
        // Stage organizer/nominee notifications BEFORE the single SaveChanges below: the sender only
        // stages outbox rows, so anything enqueued after the save would be silently dropped.
        var calendarEvent = await events.GetByIdForTenantAsync(tenantId, request.EventId, ct);
        if (calendarEvent is not null)
        {
            var responderName = $"{employee.FirstName} {employee.LastName}";
            switch (normalizedStatus)
            {
                case CalendarEventParticipantStatuses.Accepted:
                case CalendarEventParticipantStatuses.Rejected:
                    await notifications.NotifyRsvpAsync(
                        tenantId, calendarEvent.CreatedById, calendarEvent.Title, responderName,
                        accepted: normalizedStatus == CalendarEventParticipantStatuses.Accepted, ct);
                    break;
                case CalendarEventParticipantStatuses.ResolutionRequested:
                    await notifications.NotifyResolutionRequestedAsync(tenantId, calendarEvent.CreatedById, calendarEvent.Title, responderName, request.Reason!, ct);
                    break;
                case CalendarEventParticipantStatuses.ReplacementNominated:
                    await notifications.NotifyReplacementNominatedAsync(tenantId, calendarEvent.CreatedById, calendarEvent.Title, responderName, nomineeName!, ct);
                    await notifications.NotifyNomineeAsync(tenantId, request.NomineeEmployeeId!.Value, calendarEvent.Title, responderName, ct);
                    break;
            }
        }
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~RespondToCalendarEventCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/RespondToCalendarEvent tests/ONEVO.Tests.Unit/Features/Calendar/RespondToCalendarEventCommandHandlerTests.cs
git commit -m "feat(calendar): organizer hears about accepted and declined invitations; nominee is told

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: P1-4 — removing a participant or guest tells them

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/RemoveCalendarEventParticipant/RemoveCalendarEventParticipantCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/RemoveCalendarEventGuest/RemoveCalendarEventGuestCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/RemoveCalendarEventParticipantCommandHandlerTests.cs`, `RemoveCalendarEventGuestCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

In both test classes add fields and change `BuildSut`'s constructor call:

```csharp
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
```

In `BuildSut`, before `return`, add:

```csharp
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
```

and change the `return` to pass `_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object`. Add `using ONEVO.Application.Features.Calendar.Services;`.

Append to `RemoveCalendarEventParticipantCommandHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_RemovedParticipant_IsNotifiedBeforeTheSave()
    {
        var evt = MakeEvent();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = ParticipantEmployeeId };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetTrackedParticipantAsync(TenantId, EventId, ParticipantEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);
        var calls = new List<string>();
        _notifications.Setup(n => n.NotifyParticipantRemovedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        var result = await BuildSut().Handle(new RemoveCalendarEventParticipantCommand(EventId, ParticipantEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Verify(n => n.NotifyParticipantRemovedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location, ParticipantEmployeeId, "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        calls.Should().Equal("notify", "save");
    }
```

Append to `RemoveCalendarEventGuestCommandHandlerTests` (the file's `MakeEvent()` and guest setup mirror the participant file; `GetTrackedGuestAsync(TenantId, EventId, "vendor@example.com", …)`):

```csharp
    [Fact]
    public async Task Handle_RemovedGuest_IsEmailedBeforeTheSave()
    {
        var evt = MakeEvent();
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetTrackedGuestAsync(TenantId, EventId, "vendor@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(guest);
        var calls = new List<string>();
        _notifications.Setup(n => n.NotifyGuestRemovedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        var result = await BuildSut().Handle(new RemoveCalendarEventGuestCommand(EventId, "Vendor@Example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Verify(n => n.NotifyGuestRemovedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location, "vendor@example.com", "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        calls.Should().Equal("notify", "save");
    }
```

If the guest test file has no `MakeEvent()`, copy the participant file's `MakeEvent()` into it verbatim.

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: constructor arity errors (the handlers take 3 arguments).

- [ ] **Step 2: Implement**

`RemoveCalendarEventParticipantCommandHandler`: the primary constructor becomes

```csharp
public sealed class RemoveCalendarEventParticipantCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork)
```

and the end of `Handle` (from `events.RemoveParticipant(participant);`) becomes:

```csharp
        var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
        // Staged before the save so it commits with the removal.
        await notifications.NotifyParticipantRemovedAsync(
            tenantId, existing.Title, existing.StartDate, existing.Location, participant.EmployeeId, organizerName, ct);

        events.RemoveParticipant(participant);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
```

`RemoveCalendarEventGuestCommandHandler`: the same constructor change, and the end of `Handle` (from `events.RemoveGuest(guest);`) becomes:

```csharp
        var callerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = callerEmployee is null ? "Someone" : $"{callerEmployee.FirstName} {callerEmployee.LastName}";
        // Staged before the save so it commits with the removal.
        await notifications.NotifyGuestRemovedAsync(
            tenantId, existing.Title, existing.StartDate, existing.Location, guest.Email, organizerName, ct);

        events.RemoveGuest(guest);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
```

Add `using ONEVO.Application.Features.Calendar.Services;` to both.

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~RemoveCalendarEvent"`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/RemoveCalendarEventParticipant src/ONEVO.Application/Features/Calendar/Commands/RemoveCalendarEventGuest tests/ONEVO.Tests.Unit/Features/Calendar/RemoveCalendarEventParticipantCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/RemoveCalendarEventGuestCommandHandlerTests.cs
git commit -m "feat(calendar): removed participants and guests are told

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Integration tests — the outbox rows really exist after each command returns (spec §3.4)

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationDb.cs`
- Create: `tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationOutboxIntegrationTests.cs`

**Interfaces:**
- Consumes: every handler from Tasks 3–8 with their real dependencies.

- [ ] **Step 1: Create the harness**

`tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationDb.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.DTOs;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using EmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.Calendar;

/// <summary>Real PostgreSQL, real repositories, real OutboxWriter and CalendarNotificationSender.
/// Handlers are built per call on a fresh context, the way a request scope would build them.</summary>
public sealed class CalendarNotificationDb
{
    public sealed class StubCurrentUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => "organizer@calendar-test.onevo.dev";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    /// <summary>Delete never reaches these: the harness events have no online meeting.</summary>
    private sealed class UnusedProviderClients : ITeamsMeetingClient, IZoomMeetingClient, ICalendarConnectionTokenProvider
    {
        Task<TeamsMeetingDto> ITeamsMeetingClient.CreateMeetingAsync(string a, string s, DateTimeOffset b, DateTimeOffset e, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task ITeamsMeetingClient.CancelMeetingAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<IReadOnlyList<TeamsAttendanceRecordDto>> ITeamsMeetingClient.GetAttendanceAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<ZoomMeetingDto> IZoomMeetingClient.CreateMeetingAsync(string a, string s, DateTimeOffset b, DateTimeOffset e, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task IZoomMeetingClient.CancelMeetingAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<IReadOnlyList<ZoomAttendanceRecordDto>> IZoomMeetingClient.GetAttendanceAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<string?> ICalendarConnectionTokenProvider.GetFreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => throw new InvalidOperationException("not used");
    }

    public static readonly AesEncryptionService Encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public Guid TenantId { get; private set; }
    public Employee Organizer { get; private set; } = null!;
    public StubCurrentUser OrganizerUser => new(TenantId, Organizer.UserId);
    public object Unused { get; } = new UnusedProviderClients();

    public static async Task<CalendarNotificationDb> CreateAsync()
    {
        var h = new CalendarNotificationDb { _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync() };
        h.TenantId = Guid.NewGuid();
        h.Organizer = NewEmployee(h.TenantId, "Ada", "Owner");
        await using var db = h.Context(new StubCurrentUser(h.TenantId, h.Organizer.UserId));
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == 1))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.Tenants.Add(new Tenant
        {
            Id = h.TenantId, Name = "Calendar Tenant", Slug = $"cal-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "1-10", Status = TenantStatus.Active
        });
        db.Employees.Add(h.Organizer);
        await db.SaveChangesAsync();
        return h;
    }

    private static Employee NewEmployee(Guid tenantId, string first, string last) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(),
        EmployeeNumber = $"CAL-{Guid.NewGuid():N}"[..12], FirstName = first, LastName = last,
        Email = $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@calendar-test.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), EmploymentStatusId = 1
    };

    public ApplicationDbContext Context(ICurrentUser? auditUser = null)
    {
        var tenantContext = new TenantContextAccessor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(auditUser ?? OrganizerUser, _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }

    public async Task<Employee> AddEmployeeAsync(string first, string last)
    {
        await using var db = Context();
        var employee = NewEmployee(TenantId, first, last);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    /// <summary>The organizer's event with the given participants and guests, created directly.</summary>
    public async Task<CalendarEvent> AddEventAsync(IReadOnlyList<Employee> participants, IReadOnlyList<string> guestEmails, bool recurring = false)
    {
        await using var db = Context();
        var start = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(9);
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Quarterly review", StartDate = new DateTimeOffset(start, TimeSpan.Zero),
            EndDate = new DateTimeOffset(start.AddHours(1), TimeSpan.Zero), SourceType = CalendarEventSourceTypes.Manual,
            Recurrence = recurring ? CalendarRecurrences.Weekly : CalendarRecurrences.None,
            RecurrenceRule = recurring ? "FREQ=WEEKLY" : null
        };
        db.CalendarEvents.Add(calendarEvent);
        foreach (var participant in participants)
            db.CalendarEventParticipants.Add(new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EventId = calendarEvent.Id, EmployeeId = participant.Id,
                ResponseStatus = CalendarEventParticipantStatuses.Pending
            });
        foreach (var email in guestEmails)
            db.CalendarEventGuests.Add(new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = calendarEvent.Id, Email = email });
        await db.SaveChangesAsync();
        return calendarEvent;
    }

    public sealed record CalendarHandlers(
        ApplicationDbContext Db, EfCalendarEventRepository Events, EmployeeRepository Employees,
        CalendarNotificationSender Sender, UnitOfWork UnitOfWork) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    /// <summary>One "request scope": a context and the real collaborators handlers depend on.</summary>
    public CalendarHandlers Scope(ICurrentUser? caller = null)
    {
        var db = Context(caller ?? OrganizerUser);
        var employees = new EmployeeRepository(db);
        var sender = new CalendarNotificationSender(new OutboxWriter(db, Encryption, _clock), employees);
        return new CalendarHandlers(db, new EfCalendarEventRepository(db), employees, sender, new UnitOfWork(db));
    }

    public sealed record OutboxRow(string Type, string? DeduplicationKey, JsonDocument Payload);

    public async Task<IReadOnlyList<OutboxRow>> OutboxAsync()
    {
        await using var db = Context();
        var rows = await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.TenantId == TenantId).OrderBy(m => m.CreatedAt).ToListAsync();
        return rows.Select(m => new OutboxRow(m.Type, m.DeduplicationKey, JsonDocument.Parse(Encryption.Decrypt(m.EncryptedPayload)))).ToList();
    }

    public static IEnumerable<OutboxRow> Emails(IEnumerable<OutboxRow> rows, string kind) =>
        rows.Where(r => r.Type == OutboxMessageTypes.CalendarEventInviteEmail
            && (r.Payload.RootElement.TryGetProperty("Kind", out var k) ? k.GetString() : CalendarEmailKinds.Invite) == kind);

    public static IEnumerable<OutboxRow> InApp(IEnumerable<OutboxRow> rows, string templateCode) =>
        rows.Where(r => r.Type == OutboxMessageTypes.WorkNotification && r.Payload.RootElement.GetProperty("TemplateCode").GetString() == templateCode);
}
```

Verify the namespaces against the code before compiling: `grep -rn "namespace" src/ONEVO.Application/Features/Calendar/DTOs/*Teams* src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs src/ONEVO.Infrastructure/Persistence/UnitOfWork.cs | head`. If a DTO such as `TeamsMeetingDto` lives elsewhere, fix that `using` only. The `DbSet` names (`CalendarEvents`, `CalendarEventParticipants`, `CalendarEventGuests`) come from `ApplicationDbContext`; check them with `grep -n "DbSet<CalendarEvent" src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`.

- [ ] **Step 2: Write the integration tests**

`tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationOutboxIntegrationTests.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventParticipants;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventParticipant;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.Calendar.CalendarNotificationDb;

namespace ONEVO.Tests.Integration.Calendar;

public sealed class CalendarNotificationOutboxIntegrationTests
{
    [Fact]
    public async Task Update_OutboxRowsExistAfterTheCommandReturns_InAppAndUpdatedEmailForParticipantAndGuest()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], ["vendor@example.com"]);

        await using (var s = h.Scope())
        {
            var result = await new UpdateCalendarEventCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork).Handle(
                new UpdateCalendarEventCommand(evt.Id, "Quarterly review (moved)", null, evt.StartDate.AddHours(1), evt.EndDate.AddHours(1),
                    false, null, null, null, CalendarRecurrences.None),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var rows = await h.OutboxAsync();
        Assert.Single(InApp(rows, "calendar_event_updated"));
        var emails = Emails(rows, CalendarEmailKinds.Updated).ToList();
        Assert.Equal(2, emails.Count);
        Assert.All(emails, e => Assert.StartsWith($"update:{evt.Id}:1:", e.DeduplicationKey));
    }

    [Fact]
    public async Task EditAllOccurrences_OutboxRowsExistAfterTheCommandReturns()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var series = await h.AddEventAsync([participant], ["vendor@example.com"], recurring: true);

        await using (var s = h.Scope())
        {
            var result = await new EditRecurringOccurrenceCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork).Handle(
                new EditRecurringOccurrenceCommand(series.Id, RecurrenceEditScope.AllEvents, series.StartDate, "Weekly review",
                    null, series.StartDate, series.EndDate, false, null, null, null),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var rows = await h.OutboxAsync();
        Assert.Single(InApp(rows, "calendar_event_updated"));
        Assert.Equal(2, Emails(rows, CalendarEmailKinds.Updated).Count());
    }

    [Fact]
    public async Task CancelOccurrenceTwice_EmailsEachRecipientOnce()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var series = await h.AddEventAsync([participant], ["vendor@example.com"], recurring: true);
        var occurrence = series.StartDate.AddDays(7);

        for (var i = 0; i < 2; i++)
        {
            await using var s = h.Scope();
            var result = await new CancelRecurringOccurrenceCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork)
                .Handle(new CancelRecurringOccurrenceCommand(series.Id, occurrence), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var cancelled = Emails(await h.OutboxAsync(), CalendarEmailKinds.Cancelled).ToList();
        Assert.Equal(2, cancelled.Count); // participant + guest, not 4
        Assert.All(cancelled, e => Assert.StartsWith($"cancel:{series.Id}:{occurrence.UtcTicks}:", e.DeduplicationKey));
    }

    [Fact]
    public async Task Delete_EmailsTheGuestToo()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], ["vendor@example.com"]);

        await using (var s = h.Scope())
        {
            var unused = h.Unused;
            var result = await new DeleteCalendarEventCommandHandler(
                    h.OrganizerUser, s.Events, s.Employees, s.Sender,
                    new EfCalendarEventMeetingRepository(s.Db), new EfExternalCalendarConnectionRepository(s.Db),
                    (ICalendarConnectionTokenProvider)unused, (ITeamsMeetingClient)unused, (IZoomMeetingClient)unused, s.UnitOfWork)
                .Handle(new DeleteCalendarEventCommand(evt.Id), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var cancelled = Emails(await h.OutboxAsync(), CalendarEmailKinds.Cancelled).ToList();
        Assert.Contains(cancelled, e => e.Payload.RootElement.GetProperty("ToEmail").GetString() == "vendor@example.com");
        Assert.Equal(2, cancelled.Count);
    }

    [Fact]
    public async Task Accept_OrganizerInAppRowExistsAfterTheCommandReturns()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], []);

        await using (var s = h.Scope(new StubCurrentUser(h.TenantId, participant.UserId)))
        {
            var result = await new RespondToCalendarEventCommandHandler(
                    new StubCurrentUser(h.TenantId, participant.UserId), s.Events, s.Employees, s.UnitOfWork, s.Sender)
                .Handle(new RespondToCalendarEventCommand(evt.Id, "Accepted"), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var accepted = Assert.Single(InApp(await h.OutboxAsync(), "calendar_event_rsvp_accepted"));
        Assert.Equal(h.Organizer.UserId, accepted.Payload.RootElement.GetProperty("RecipientUserId").GetGuid());
    }

    [Fact]
    public async Task RemoveThenReAddParticipant_SendsARemovedEmailAndASecondInvite()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([], []);

        async Task AddAsync()
        {
            await using var s = h.Scope();
            var result = await new AddCalendarEventParticipantsCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork)
                .Handle(new AddCalendarEventParticipantsCommand(evt.Id, [participant.Id]), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await AddAsync();
        await using (var s = h.Scope())
        {
            var result = await new RemoveCalendarEventParticipantCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, s.UnitOfWork)
                .Handle(new RemoveCalendarEventParticipantCommand(evt.Id, participant.Id), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }
        await AddAsync();

        var rows = await h.OutboxAsync();
        var invites = Emails(rows, CalendarEmailKinds.Invite).ToList();
        Assert.Equal(2, invites.Count);
        Assert.NotEqual(invites[0].DeduplicationKey, invites[1].DeduplicationKey);
        Assert.Single(Emails(rows, CalendarEmailKinds.Removed));
        Assert.Single(InApp(rows, "calendar_event_participant_removed"));
    }
}
```

Check the repository class names and namespaces with `grep -rn "class EfCalendarEventMeetingRepository\|class EfExternalCalendarConnectionRepository" src`, and fix the `using` if needed.

- [ ] **Step 3: Run them**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~CalendarNotificationOutboxIntegrationTests"`
Expected: 6 passed. (Match `EditRecurringOccurrenceCommand`'s argument order to its record definition, as in Task 5.) A failure here means a handler still stages after its save, or a keyed email is outside the transaction. Fix the handler, not the test.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Calendar
git commit -m "test(calendar): outbox rows exist in the database after each notifying command returns

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Full verification and push

- [ ] **Step 1: Backend build, unit, architecture**

```bash
cd "C:/Users/User/onexso-worktrees/be-meeting-task"
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo 2>&1 | tail -3
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | tail -3
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -nologo 2>&1 | tail -3
```
Expected: 0 errors; both suites `Failed: 0`.

- [ ] **Step 2: Integration, affected areas**

```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~Calendar|FullyQualifiedName~Outbox|FullyQualifiedName~E2E" 2>&1 | tail -3
```
Expected: `Failed: 0`. The full suite's two known My Team failures are documented in `docs/superpowers/reports/2026-10-14-meeting-task-phase1-closure.md` §4 and are not in this filter.

- [ ] **Step 3: Push (no PR)**

```bash
git push origin feature/meeting-task-phase0-1
```
