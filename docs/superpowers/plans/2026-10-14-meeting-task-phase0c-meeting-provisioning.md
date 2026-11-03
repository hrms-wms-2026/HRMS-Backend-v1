# Meetings as Tasks — Phase 0C (Online-meeting provisioning, cancellation and link emails) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Creating a Teams/Zoom link never duplicates a meeting or an email, never loses track of a meeting the provider may have created, and never leaves people uninvited.
- Deleting an event cancels its remote meeting only after the delete commits.

**Architecture:**
- **Claiming:** one meeting row per event (existing unique index) carries a link *generation* and a provisioning state. A request claims the row atomically before any provider call: an `INSERT … ON CONFLICT DO NOTHING`, or a conditional `UPDATE … RETURNING`. The loser makes no provider call.
- **Provider call:** an `IMeetingProvisioner` runs it within a 90 s budget, refreshing the token once on 401 and honouring 429, and classifies the outcome.
- **Recovery:**
  - Teams uses Graph `createOrGet` with `externalId = {eventId}:{generation}`, which is idempotent at the provider.
  - Zoom tags each meeting `onexso:{eventId}:{generation}` and correlates by listing the organizer's meetings.
- **Event delete:** the remote cancel becomes a self-contained keyed outbox message that only exists if the delete commits. A permanent failure parks it as `Failed` and tells the organizer.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (raw SQL claims), MediatR, xUnit + Moq, Testcontainers PostgreSQL; Angular 21 standalone + signals, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-13-meeting-task-phase0-1-design.md` §3.2 rows P1-4 (meeting link removed), P1-5, X-1 (link keys), X-2, X-3, and all of §3.2.2.

**Order:** execute after **0A** (it consumes `EnqueueKeyedAsync`, recipient records, `CalendarEmailKeys` and `CalendarEmailKinds`) and **0B** (the claim SQL resets the `attendance_sync_status` column that 0B adds).

## Global Constraints

- Worktrees: backend `C:\Users\User\onexso-worktrees\be-meeting-task`, frontend `C:\Users\User\onexso-worktrees\fe-meeting-task`, branch `feature/meeting-task-phase0-1`.
- Statuses (`calendar_event_meetings.status`, max 20 chars): `active`, `cancelled`, `failed` (existing), plus `provisioning`, `provisioning_unknown` and `unresolved`.
- Claim rules (spec §3.2.2):
  - No row → `INSERT … status = provisioning` (a concurrent second request hits the unique index and loses).
  - `failed`/`cancelled` → `UPDATE … SET status = provisioning, link_generation = link_generation + 1 … WHERE status IN ('failed','cancelled') RETURNING`.
  - `provisioning` older than **5 min** → `UPDATE … SET status = provisioning_unknown WHERE status = 'provisioning' AND provisioning_started_at < now − 5 min`. It is then recovered by the organizer's **Retry**, same generation.
  - `provisioning_unknown` → claimable only by an explicit **Retry** (or **Remove**).
  - `unresolved` → not claimable until the organizer confirms cleanup (→ `cancelled`).
  - A losing request makes **no provider call**. `provisioning` → 409 `meeting_link_in_progress`; `active` → the existing link.
- Provider outcomes (spec table):
  - 2xx → `active`.
  - 401 → refresh once and retry once; still 401 → `failed`.
  - 429 → nothing created; honour `Retry-After`, **3 attempts total**, then `failed` (`rate_limited`).
  - 400/403/404/not connected/missing scope → `failed`.
  - timeout/5xx/connection reset → `provisioning_unknown`.
  - **A request's total provider time is capped at 90 s** (HTTP timeout, 429 waits and the 401 refresh all count).
- Every non-success outcome triggers the **P1-5 fallback**: the held-back invites go out **without a link**, and the organizer gets an in-app notice.
- Correlation:
  - Teams `externalId = {eventId}:{generation}`.
  - Zoom `agenda` token `onexso:{eventId}:{generation}`.
  - Zoom adopts a meeting **only on an exact token match**; topic and time are never used to match.
  - Zoom lists with `GET /users/me/meetings?type=scheduled`, following `next_page_token` (never `upcoming_meetings`). It fetches `GET /meetings/{id}` for meetings whose start date equals the event's when `agenda` isn't in the list.
- `provisioning_unknown` → **Remove** resolves the remote side first:
  - Teams: `createOrGet` (same `externalId`), then delete → `cancelled`.
  - Zoom: token match → delete → `cancelled`; complete list with no match → `cancelled`.
  - Correlation impossible → `unresolved` + organizer warning.
  - An event that ended while still `provisioning_unknown` becomes `unresolved` (lazily, the next time the row is read).
- Email keys:
  - `link:{meetingId}:{generation}:{participantRowId|guestRowId}`
  - `link-removed:{meetingId}:{generation}:{recipient}`
  - fallback invites `invite:{rowId}` (the same key as 0A's invite, so nobody is invited twice)
  - delete cancel message `meeting-cancel:{meetingId}:{generation}`
- Self-contained cancel payload (spec X-2): `tenantId, provider, externalMeetingId, externalCalendarConnectionId, organizerEmployeeId, eventTitle`, plus `organizerUserId` (needed to notify the organizer once the event row is gone).
- **Zoom scope prerequisite (operator action, not code):** listing meetings needs Zoom's `meeting:read:list_meetings` scope.
  - Task 1 adds it to the requested scopes, but the Zoom Marketplace app must also enable it, and existing Zoom connections must reconnect to receive it.
  - Until then Zoom correlation fails safely: Retry stays `provisioning_unknown`, and Remove becomes `unresolved` with a "check Zoom" warning. **It never creates a duplicate.**
- Graph `createOrGet` (verified 2026-10-14 against learn.microsoft.com): delegated permission `OnlineMeetings.ReadWrite` (already requested). It returns 201 for a new meeting and 200 for an existing one. The identity is (tenant, user, externalId), so a different organizer always gets a different meeting.
- Migrations: `ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none" dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`.
- Production code: no mocks or placeholders. Commit and push to the branch only, **no PR**. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Two first-time creates for the same event at once** → exactly one provider call; the other gets 409 `meeting_link_in_progress`. Tested in Task 9 (real Postgres).
2. **Link created → removed → created** → each generation's link email and link-removed email is sent once; a retried request inside a generation sends nothing new. Tested in Task 9.
3. **Event deleted, local save fails** → no cancel message exists (the remote meeting is untouched). Tested in Task 8.
4. **A Zoom connection without the list scope** → Retry and Remove never create a second meeting (correlation failure → `provisioning_unknown` / `unresolved`). Tested in Task 5.
5. **The one-click "create event with Teams link" flow when Teams fails** → the event exists, invitations went out without a link, and the organizer sees why. Tested in Tasks 6 and 10.

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/MeetingProviderException.cs` (create) | Typed provider failure |
| `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs` (modify) | New statuses, `LinkGeneration`, `ProvisioningStartedAt`, `FailureReason`, `CalendarMeetingCorrelation` |
| `ITeamsMeetingClient.cs`, `IZoomMeetingClient.cs`, `MicrosoftGraphMeetingClient.cs`, `ZoomMeetingClient.cs` (modify) | createOrGet, correlation token and lookup, classified errors |
| `ICalendarConnectionTokenProvider.cs`, `CalendarConnectionTokenProvider.cs` (modify) | `ForceRefreshAccessTokenAsync` |
| `ICalendarEventMeetingRepository.cs`, `EfCalendarEventMeetingRepository.cs` (modify) | Atomic claim SQL, untracked read |
| `src/ONEVO.Application/Features/Calendar/Services/MeetingProvisioner.cs` (create) | Provider-call policy (budget, 401, 429, recovery, remote resolution) |
| `src/ONEVO.Application/Features/Calendar/Services/EventMeetingLinkService.cs` (create) | Claim completion, link/fallback emails, lazy `unresolved` |
| `CreateEventMeeting`, new `RetryEventMeeting`, `ConfirmEventMeetingCleanup`, `GetEventMeeting` (create/modify) | Commands and query |
| `RemoveEventMeetingCommandHandler.cs` (rewrite) | Remove per status, link-removed notices |
| `DeleteCalendarEventCommandHandler.cs` (modify), `CalendarMeetingCancelOutboxHandler.cs` (create), `OutboxProcessor.cs` (modify) | After-commit cancel, park-on-permanent |
| `CalendarNotificationSender` + seeder + email renderer (modify) | Link-added (keyed), link-removed, organizer notices |
| `CalendarController.cs`, `CalendarContracts.cs` (modify) | New endpoints |
| frontend `calendar-event-api.service.ts`, `calendar-event.store.ts`, `calendar-page.component.ts`, new `calendar-event-meeting-status` component (modify/create) | Toasts and status UI |

---

### Task 1: Provider clients — `createOrGet`, correlation token, classified failures

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/MeetingProviderException.cs`
- Modify: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs` (add `CalendarMeetingCorrelation`)
- Modify: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ITeamsMeetingClient.cs`, `IZoomMeetingClient.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/MicrosoftGraphMeetingClient.cs`, `ZoomMeetingClient.cs`
- Modify: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Helpers/PlatformOAuthProviderCatalog.cs:94-96`
- Modify (compile only, rewritten in Task 6): `CreateEventMeetingCommandHandler.cs:61-68`, `tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs`, `ZoomMeetingClientTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum MeetingProviderFailureKind { Unauthorized, RateLimited, Permanent, Ambiguous }
public sealed class MeetingProviderException : Exception { MeetingProviderFailureKind Kind; TimeSpan? RetryAfter; }
public static class CalendarMeetingCorrelation { string TeamsExternalId(Guid eventId, int generation); string ZoomToken(Guid eventId, int generation); }
// ITeamsMeetingClient
Task<TeamsMeetingDto> CreateOrGetMeetingAsync(string accessToken, string externalId, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct); // 404 = already gone = success
// IZoomMeetingClient
Task<ZoomMeetingDto> CreateMeetingAsync(string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, string correlationToken, CancellationToken ct);
Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(string accessToken, string correlationToken, DateTimeOffset eventStart, CancellationToken ct);
Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct); // 404 = success; 3002 = ZoomMeetingInProgressException
```

Both clients throw `MeetingProviderException` for every non-success except the documented ones.

- [ ] **Step 1: Write the failing client tests**

In `MicrosoftGraphMeetingClientTests`, replace `CreateMeetingAsync_PostsToOnlineMeetingsEndpoint_ReturnsJoinInfo` with:

```csharp
    [Fact]
    public async Task CreateOrGetMeetingAsync_PostsToCreateOrGetWithTheExternalId_ReturnsJoinInfo()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/createOrGet", request.RequestUri!.ToString());
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { id = "graph-meeting-1", joinWebUrl = "https://teams.microsoft.com/l/meetup-join/abc" })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.CreateOrGetMeetingAsync(
            "access-token", "evt-1:2", "Sprint planning",
            new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal("graph-meeting-1", result.ExternalMeetingId);
        Assert.Contains("\"externalId\":\"evt-1:2\"", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MeetingProviderFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, MeetingProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, MeetingProviderFailureKind.Permanent)]
    [InlineData(HttpStatusCode.BadRequest, MeetingProviderFailureKind.Permanent)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MeetingProviderFailureKind.Ambiguous)]
    public async Task CreateOrGetMeetingAsync_ClassifiesFailures(HttpStatusCode status, MeetingProviderFailureKind kind)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
            if (status == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => client.CreateOrGetMeetingAsync(
            "t", "e:1", "s", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        if (kind == MeetingProviderFailureKind.RateLimited)
            Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
    }

    [Fact]
    public async Task CreateOrGetMeetingAsync_ConnectionFailure_IsAmbiguous()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => client.CreateOrGetMeetingAsync(
            "t", "e:1", "s", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.Equal(MeetingProviderFailureKind.Ambiguous, ex.Kind);
    }

    [Fact]
    public async Task CancelMeetingAsync_AlreadyGone_IsSuccess()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "graph-meeting-1", CancellationToken.None);
    }
```

In `ZoomMeetingClientTests`:
- Add `"onexso:evt:1"` as the 5th argument to both `client.CreateMeetingAsync(...)` calls.
- In `CreateMeetingAsync_PostsToMeetingsEndpoint_ReturnsJoinInfo`, assert the request body contains `"agenda":"onexso:evt:1"`: read `request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()` inside the stub, as the Graph test does.
- Replace `CancelMeetingAsync_OtherBadRequest_ThrowsGenericHttpRequestException` with a test expecting `MeetingProviderException` of kind `Permanent`.
- Replace `CancelMeetingAsync_NonJsonErrorBody_ThrowsGenericHttpRequestException` (502) with one expecting kind `Ambiguous`.

Then append:

```csharp
    [Fact]
    public async Task CancelMeetingAsync_AlreadyGone_IsSuccess()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"code":3001}""") });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None);
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_MatchOnALaterPage_ReturnsItsDetails()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url == "https://api.zoom.us/v2/users/me/meetings?type=scheduled&page_size=300")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new[] { new { id = 111L, agenda = "something else", start_time = "2026-10-20T09:00:00Z" } },
                        next_page_token = "tok2"
                    })
                };
            if (url.EndsWith("next_page_token=tok2"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new[] { new { id = 222L, agenda = "onexso:evt:1", start_time = "2026-10-20T09:00:00Z" } },
                        next_page_token = ""
                    })
                };
            Assert.Equal("https://api.zoom.us/v2/meetings/222", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = 222L, agenda = "onexso:evt:1", join_url = "https://zoom.us/j/222", password = "abc" })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var match = await client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("222", match!.ExternalMeetingId);
        Assert.Equal("https://zoom.us/j/222", match.JoinUrl);
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_NoAgendaInTheList_FetchesDetailsOnlyForSameDayMeetings()
    {
        var detailCalls = new List<string>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.zoom.us/v2/users/me/meetings"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new object[]
                        {
                            new { id = 333L, start_time = "2026-10-21T09:00:00Z" },
                            new { id = 444L, start_time = "2026-10-20T15:00:00Z" }
                        },
                        next_page_token = ""
                    })
                };
            detailCalls.Add(url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = 444L, agenda = "onexso:evt:1", join_url = "https://zoom.us/j/444" })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var match = await client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal("444", match!.ExternalMeetingId);
        Assert.Equal(new[] { "https://api.zoom.us/v2/meetings/444" }, detailCalls);
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_CompleteListWithNoMatch_ReturnsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { meetings = new[] { new { id = 1L, agenda = "onexso:other:1", start_time = "2026-10-20T09:00:00Z" } }, next_page_token = "" })
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        Assert.Null(await client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_ListForbidden_Throws()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await Assert.ThrowsAsync<MeetingProviderException>(() =>
            client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", DateTimeOffset.UtcNow, CancellationToken.None));
    }
```

Add `using ONEVO.Application.Features.Calendar.ServiceInterfaces;` to both test files.

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `MeetingProviderException` / `CreateOrGetMeetingAsync` do not exist.

- [ ] **Step 2: Implement the shared types**

`src/ONEVO.Application/Features/Calendar/ServiceInterfaces/MeetingProviderException.cs`:

```csharp
using System.Net;

namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public enum MeetingProviderFailureKind
{
    /// <summary>401: refresh the token once and retry once.</summary>
    Unauthorized,
    /// <summary>429: the provider rejected the request, so nothing was created; safe to retry after Retry-After.</summary>
    RateLimited,
    /// <summary>400/403/404 and other 4xx: retrying won't help.</summary>
    Permanent,
    /// <summary>Timeout, 5xx or connection failure: the meeting may or may not exist remotely.</summary>
    Ambiguous
}

/// <summary>A Teams/Zoom meeting API call failed; <see cref="Kind"/> drives what the caller may do next (spec §3.2.2).</summary>
public sealed class MeetingProviderException(
    MeetingProviderFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public MeetingProviderFailureKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public static MeetingProviderFailureKind Classify(HttpStatusCode status) => (int)status switch
    {
        401 => MeetingProviderFailureKind.Unauthorized,
        429 => MeetingProviderFailureKind.RateLimited,
        >= 500 => MeetingProviderFailureKind.Ambiguous,
        _ => MeetingProviderFailureKind.Permanent
    };
}
```

In `CalendarEventMeeting.cs`, add at namespace level:

```csharp
/// <summary>Provider-side identities for one link generation of an event (spec §3.2.2). The same
/// (event, generation) always maps to the same remote meeting, so a retry adopts it instead of
/// creating a second one.</summary>
public static class CalendarMeetingCorrelation
{
    public static string TeamsExternalId(Guid eventId, int generation) => $"{eventId}:{generation}";
    public static string ZoomToken(Guid eventId, int generation) => $"onexso:{eventId}:{generation}";
}
```

In `ITeamsMeetingClient`, replace `CreateMeetingAsync` with:

```csharp
    /// <summary>Graph POST /me/onlineMeetings/createOrGet: returns the organizer's existing meeting with
    /// this externalId, or creates it. Throws MeetingProviderException on failure.</summary>
    Task<TeamsMeetingDto> CreateOrGetMeetingAsync(
        string accessToken, string externalId, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
```

In `IZoomMeetingClient`, replace `CreateMeetingAsync` with the two methods below:

```csharp
    /// <summary>Creates a scheduled meeting whose agenda is <paramref name="correlationToken"/>.
    /// Throws MeetingProviderException on failure.</summary>
    Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, string correlationToken, CancellationToken ct);

    /// <summary>The organizer's scheduled meeting whose agenda is exactly <paramref name="correlationToken"/>,
    /// or null when every page was read and none matches. Throws MeetingProviderException when the list
    /// can't be read completely (then nothing is known).</summary>
    Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(
        string accessToken, string correlationToken, DateTimeOffset eventStart, CancellationToken ct);
```

- [ ] **Step 3: Implement the Graph client**

In `MicrosoftGraphMeetingClient`, add `using System.Net;` and replace `CreateMeetingAsync` and `CancelMeetingAsync` with:

```csharp
    public async Task<TeamsMeetingDto> CreateOrGetMeetingAsync(
        string accessToken, string externalId, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/onlineMeetings/createOrGet");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            externalId,
            startDateTime = start.UtcDateTime.ToString("o"),
            endDateTime = end.UtcDateTime.ToString("o"),
            subject
        });
        using var response = await SendClassifiedAsync(request, "create or get online meeting", ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var externalMeetingId = root.GetProperty("id").GetString()!;
        var joinUrl = root.GetProperty("joinWebUrl").GetString()!;
        string? passcode = null;
        if (root.TryGetProperty("joinInformation", out var joinInfo)
            && joinInfo.ValueKind != JsonValueKind.Null
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
        // 404: already deleted - the goal state, not a failure.
        using var _ = await SendClassifiedAsync(request, "cancel online meeting", ct, HttpStatusCode.NotFound);
    }

    /// <summary>Sends and returns the response for 2xx (or an accepted status), otherwise logs Graph's
    /// error body and throws a MeetingProviderException classified for the caller's retry decision.</summary>
    private async Task<HttpResponseMessage> SendClassifiedAsync(
        HttpRequestMessage request, string operation, CancellationToken ct, params HttpStatusCode[] acceptedStatuses)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Microsoft Graph {operation} failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Microsoft Graph {operation} timed out.", innerException: ex);
        }

        if (response.IsSuccessStatusCode || acceptedStatuses.Contains(response.StatusCode))
            return response;

        using (response)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Microsoft Graph {Operation} returned {StatusCode}: {ErrorBody}", operation, (int)response.StatusCode, errorBody);
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            throw new MeetingProviderException(
                MeetingProviderException.Classify(response.StatusCode),
                $"Microsoft Graph {operation} returned {(int)response.StatusCode}.", retryAfter);
        }
    }
```

- [ ] **Step 4: Implement the Zoom client**

In `ZoomMeetingClient`, add `using System.Net;`. Change `CreateMeetingAsync`'s signature to take `string correlationToken` before `CancellationToken ct`, add `agenda = correlationToken,` after `topic = subject,` in the request body, and replace its `using var response = await httpClient.SendAsync(request, ct); await EnsureSuccessOrLogAsync(response, "create meeting", ct);` with `using var response = await SendClassifiedAsync(request, "create meeting", ct);`. Replace `CancelMeetingAsync` with:

```csharp
    public async Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.zoom.us/v2/meetings/{externalMeetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom cancel meeting failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "Zoom cancel meeting timed out.", innerException: ex);
        }

        using (response)
        {
            // 404: already deleted - the goal state, not a failure.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return;

            // Zoom code 3002 ("Meeting is in progress"): user-recoverable, kept as its own exception.
            if (await TryReadZoomErrorCodeAsync(response, ct) == 3002)
            {
                await LogErrorBodyAsync(response, "cancel meeting", ct);
                throw new ZoomMeetingInProgressException();
            }

            await LogErrorBodyAsync(response, "cancel meeting", ct);
            throw new MeetingProviderException(
                MeetingProviderException.Classify(response.StatusCode), $"Zoom cancel meeting returned {(int)response.StatusCode}.",
                RetryAfterOf(response));
        }
    }

    private const int MaxListPages = 100;

    public async Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(
        string accessToken, string correlationToken, DateTimeOffset eventStart, CancellationToken ct)
    {
        string? pageToken = null;
        for (var page = 1; ; page++)
        {
            if (page > MaxListPages)
                throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom meeting list exceeded {MaxListPages} pages.");

            var url = "https://api.zoom.us/v2/users/me/meetings?type=scheduled&page_size=300"
                + (string.IsNullOrEmpty(pageToken) ? "" : $"&next_page_token={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendClassifiedAsync(request, "list meetings", ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var meeting in doc.RootElement.GetProperty("meetings").EnumerateArray())
            {
                var id = meeting.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
                if (meeting.TryGetProperty("agenda", out var agenda) && agenda.ValueKind == JsonValueKind.String)
                {
                    if (agenda.GetString() == correlationToken)
                        return (await GetMeetingAsync(accessToken, id, ct)).Meeting;
                    continue;
                }

                // The list didn't carry the agenda: only same-day meetings can be ours, so only fetch those.
                var start = meeting.TryGetProperty("start_time", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetDateTimeOffset() : (DateTimeOffset?)null;
                if (start is null || start.Value.UtcDateTime.Date != eventStart.UtcDateTime.Date)
                    continue;
                var details = await GetMeetingAsync(accessToken, id, ct);
                if (details.Agenda == correlationToken)
                    return details.Meeting;
            }

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(pageToken))
                return null;
        }
    }

    private async Task<(string? Agenda, ZoomMeetingDto Meeting)> GetMeetingAsync(string accessToken, string meetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.zoom.us/v2/meetings/{meetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendClassifiedAsync(request, "get meeting", ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var agenda = root.TryGetProperty("agenda", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        var passcode = root.TryGetProperty("password", out var p) ? p.GetString() : null;
        return (agenda, new ZoomMeetingDto(
            root.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            root.GetProperty("join_url").GetString()!, OrganizerJoinUrl: null, passcode));
    }

    private async Task<HttpResponseMessage> SendClassifiedAsync(HttpRequestMessage request, string operation, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom {operation} failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom {operation} timed out.", innerException: ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            await LogErrorBodyAsync(response, operation, ct);
            throw new MeetingProviderException(
                MeetingProviderException.Classify(response.StatusCode), $"Zoom {operation} returned {(int)response.StatusCode}.",
                RetryAfterOf(response));
        }
    }

    private async Task LogErrorBodyAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var errorBody = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Zoom {Operation} returned {StatusCode}: {ErrorBody}", operation, (int)response.StatusCode, errorBody);
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
```

Keep `TryReadZoomErrorCodeAsync`. Keep `EnsureSuccessOrLogAsync`, which 0B's attendance code still uses.

- [ ] **Step 5: Request the Zoom list scope**

In `PlatformOAuthProviderCatalog.cs`, change the Zoom scope list's last line to:

```csharp
                    "meeting:read:meeting", "meeting:read:list_past_participants",
                    // Correlating a meeting whose creation timed out (spec §3.2.2) lists the organizer's
                    // scheduled meetings. Also enable it on the Zoom Marketplace app; existing connections
                    // pick it up on reconnect, and until then correlation fails safely (no duplicate).
                    "meeting:read:list_meetings"
```

If a unit test pins the Zoom scope list (`grep -rn "list_past_participants" tests --include=*.cs`), add `"meeting:read:list_meetings"` to its expectation.

- [ ] **Step 6: Keep the old handler compiling (rewritten in Task 6)**

In `CreateEventMeetingCommandHandler.cs`, change the Zoom call to `zoomClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, CalendarMeetingCorrelation.ZoomToken(existing.Id, 1), ct)`. Change the Teams call to `teamsClient.CreateOrGetMeetingAsync(accessToken, CalendarMeetingCorrelation.TeamsExternalId(existing.Id, 1), existing.Title, existing.StartDate, existing.EndDate, ct)`.

In `CreateEventMeetingCommandHandlerTests.cs`:
- replace `t.CreateMeetingAsync("access-token", evt.Title,` with `t.CreateOrGetMeetingAsync("access-token", It.IsAny<string>(), evt.Title,`;
- replace `z.CreateMeetingAsync("zoom-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>())` with `z.CreateMeetingAsync("zoom-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<string>(), It.IsAny<CancellationToken>())`. Apply the same change for `"access-token"`;
- in the `Times.Never` verify, add `It.IsAny<string>(),` before `It.IsAny<CancellationToken>()`.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~MeetingClientTests|FullyQualifiedName~CreateEventMeetingCommandHandlerTests|FullyQualifiedName~OAuth"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs src/ONEVO.Infrastructure/ExternalServices/Calendar src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Helpers/PlatformOAuthProviderCatalog.cs src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting tests/ONEVO.Tests.Unit
git commit -m "feat(calendar-meetings): Teams createOrGet, Zoom correlation token and lookup, classified provider failures

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Force a token refresh after a 401

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarConnectionTokenProvider.cs`
- Modify: the `CalendarConnectionTokenProvider` class. Find it with `grep -rl "class CalendarConnectionTokenProvider" src`.
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarConnectionTokenProviderTests.cs`

**Interfaces:**
- Produces: `Task<string?> ICalendarConnectionTokenProvider.ForceRefreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)`. It refreshes even when the stored token hasn't expired. It returns null (and marks re-auth) on failure, exactly like `GetFreshAccessTokenAsync`.

- [ ] **Step 1: Write the failing test**

Append to `CalendarConnectionTokenProviderTests`:

```csharp
    [Fact]
    public async Task ForceRefreshAccessTokenAsync_TokenNotExpired_RefreshesAnyway()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), AccessTokenEncrypted = [1], RefreshTokenEncrypted = [9, 9, 9],
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        _appResolver.Setup(a => a.GetActiveAppForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("microsoft", "client-id", "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", ["scope"]));
        _appResolver.Setup(a => a.GetActiveCredentialForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("microsoft", "client-id", "client-secret", null, 1));
        _encryption.Setup(e => e.DecryptBytes(connection.RefreshTokenEncrypted)).Returns("old-refresh-token");
        _tokenExchange.Setup(t => t.RefreshTokenAsync(
                "https://login.microsoftonline.com/common/oauth2/v2.0/token", "client-id", "client-secret", "old-refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderTokens("forced-access-token", "new-refresh-token", DateTimeOffset.UtcNow.AddHours(1)));
        _encryption.Setup(e => e.EncryptBytes(It.IsAny<string>())).Returns([7]);

        var result = await BuildSut().ForceRefreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Equal("forced-access-token", result);
    }
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -2` → Expected: `ForceRefreshAccessTokenAsync` does not exist.

- [ ] **Step 2: Implement**

Add to the interface:

```csharp
    /// <summary>Refreshes even if the stored token looks valid - for a provider that just answered 401.
    /// Null (and the connection marked re-auth) when the refresh fails.</summary>
    Task<string?> ForceRefreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct);
```

In the implementation, move everything from `try` to the end of `GetFreshAccessTokenAsync` into a new `private async Task<string?> RefreshAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)`. Make `GetFreshAccessTokenAsync` end with `return await RefreshAsync(connection, oauthProvider, ct);`, and add:

```csharp
    public Task<string?> ForceRefreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
        => RefreshAsync(connection, oauthProvider, ct);
```

Any hand-written `ICalendarConnectionTokenProvider` fake in tests (for example 0A's `UnusedProviderClients` in `tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationDb.cs`) gets the member: `Task<string?> ICalendarConnectionTokenProvider.ForceRefreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => throw new InvalidOperationException("not used");`. In the same fake, rename the Teams `CreateMeetingAsync` member to `CreateOrGetMeetingAsync(string a, string externalId, string s, DateTimeOffset b, DateTimeOffset e, CancellationToken ct)`. Add `string correlationToken` to the Zoom `CreateMeetingAsync` member, and add a `FindMeetingByCorrelationTokenAsync` member that throws.

- [ ] **Step 3: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~CalendarConnectionTokenProviderTests"` → all pass. `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo` → succeeds.

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/ICalendarConnectionTokenProvider.cs src/ONEVO.Infrastructure/Services/Calendar tests
git commit -m "feat(calendar-connections): force a token refresh after a provider 401

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Durable provisioning state and atomic claims

**Files:**
- Modify: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Calendar/CalendarEventMeetingConfiguration.cs`
- Create (generated): `src/ONEVO.Infrastructure/Migrations/<ts>_AddMeetingProvisioningState.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs`, `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs`
- Create: `tests/ONEVO.Tests.Integration/Calendar/MeetingClaimIntegrationTests.cs`

**Interfaces:**
- Produces:

```csharp
// CalendarEventMeetingStatuses: + Provisioning = "provisioning", ProvisioningUnknown = "provisioning_unknown", Unresolved = "unresolved"
// CalendarEventMeeting: + int LinkGeneration (default 1), DateTimeOffset? ProvisioningStartedAt, string? FailureReason
// ICalendarEventMeetingRepository:
Task<CalendarEventMeeting?> GetByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default); // no tracking
Task<bool> TryInsertProvisioningAsync(CalendarEventMeeting row, CancellationToken ct = default);
Task<int?> TryClaimNewGenerationAsync(Guid tenantId, Guid meetingId, Guid connectionId, string provider, DateTimeOffset now, CancellationToken ct = default);
Task<bool> TryMarkStaleProvisioningUnknownAsync(Guid tenantId, Guid meetingId, DateTimeOffset staleBefore, DateTimeOffset now, CancellationToken ct = default);
Task<bool> TryClaimUnknownAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default);
Task<bool> TryMarkUnknownUnresolvedAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default);
```

Each claim/mark statement auto-commits on its own (no open transaction), so other requests see the claim before the provider call.

- [ ] **Step 1: Model**

In `CalendarEventMeetingStatuses` add:

```csharp
    /// <summary>A request owns the row and is calling the provider (claimed atomically).</summary>
    public const string Provisioning = "provisioning";
    /// <summary>The provider call was ambiguous (timeout/5xx) or its request vanished: the meeting may exist remotely.</summary>
    public const string ProvisioningUnknown = "provisioning_unknown";
    /// <summary>A meeting may still exist remotely and couldn't be found; only the organizer's confirmation clears it.</summary>
    public const string Unresolved = "unresolved";
```

In the class add:

```csharp
    /// <summary>Starts at 1; +1 for each genuinely new link. Retries of the same create reuse it, so the
    /// provider-side identity (CalendarMeetingCorrelation) and the link email keys stay the same.</summary>
    public int LinkGeneration { get; set; } = 1;
    public DateTimeOffset? ProvisioningStartedAt { get; set; }
    /// <summary>Why the last attempt didn't produce a link (auth_failed, rate_limited, rejected,
    /// not_connected, timeout_or_server_error, correlation_unavailable).</summary>
    public string? FailureReason { get; set; }
```

In the configuration add `builder.Property(m => m.LinkGeneration).HasDefaultValue(1);` and `builder.Property(m => m.FailureReason).HasMaxLength(40);`. Then generate:

```bash
ConnectionStrings__MigrationConnection="Host=localhost;Database=design_time_only;Username=none" dotnet ef migrations add AddMeetingProvisioningState --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```
Expected: three `AddColumn` calls (`link_generation` int default 1, `provisioning_started_at`, `failure_reason`).

- [ ] **Step 2: Write the failing claim tests (real Postgres)**

`tests/ONEVO.Tests.Integration/Calendar/MeetingClaimIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Integration.Calendar;

/// <summary>Uses 0A's CalendarNotificationDb harness (real PostgreSQL, one database per test).</summary>
public sealed class MeetingClaimIntegrationTests
{
    private static CalendarEventMeeting NewRow(Guid tenantId, Guid eventId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = eventId, ExternalCalendarConnectionId = Guid.NewGuid(),
        Provider = CalendarEventMeetingProviders.MicrosoftTeams, Status = CalendarEventMeetingStatuses.Provisioning,
        LinkGeneration = 1, ProvisioningStartedAt = now, CreatedAt = now
    };

    private static EfCalendarEventMeetingRepository Repo(ApplicationDbContext db) => new(db, new SystemDateTimeProvider());

    [Fact]
    public async Task TwoConcurrentFirstClaims_ExactlyOneWins()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var eventId = (await h.AddEventAsync([], [])).Id;

        var now = DateTimeOffset.UtcNow;
        async Task<bool> ClaimAsync()
        {
            await using var db = h.Context();
            return await Repo(db).TryInsertProvisioningAsync(NewRow(h.TenantId, eventId, now));
        }

        var results = await Task.WhenAll(ClaimAsync(), ClaimAsync());

        Assert.Single(results, r => r);
        await using var verify = h.Context();
        Assert.Equal(1, await verify.CalendarEventMeetings.CountAsync(m => m.CalendarEventId == eventId));
    }

    [Fact]
    public async Task NewGeneration_OnlyFromFailedOrCancelled_ConcurrentClaimsHaveOneWinner()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var evt = await h.AddEventAsync([], []);
        CalendarEventMeeting row;
        await using (var db = h.Context())
        {
            row = NewRow(h.TenantId, evt.Id, now);
            row.Status = CalendarEventMeetingStatuses.Failed;
            row.ExternalMeetingId = "";
            row.JoinUrl = "";
            db.CalendarEventMeetings.Add(row);
            await db.SaveChangesAsync();
        }

        async Task<int?> ClaimAsync()
        {
            await using var db = h.Context();
            return await Repo(db).TryClaimNewGenerationAsync(h.TenantId, row.Id, Guid.NewGuid(), CalendarEventMeetingProviders.Zoom, now);
        }

        var results = await Task.WhenAll(ClaimAsync(), ClaimAsync());

        Assert.Single(results, r => r == 2);
        Assert.Single(results, r => r is null);
        await using var verify = h.Context();
        var stored = await verify.CalendarEventMeetings.AsNoTracking().SingleAsync(m => m.Id == row.Id);
        Assert.Equal(CalendarEventMeetingStatuses.Provisioning, stored.Status);
        Assert.Equal(2, stored.LinkGeneration);
        Assert.Equal(CalendarEventMeetingProviders.Zoom, stored.Provider);
    }

    [Fact]
    public async Task StaleProvisioning_BecomesUnknown_FreshProvisioningDoesNot()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var stale = NewRow(h.TenantId, (await h.AddEventAsync([], [])).Id, now.AddMinutes(-6));
        var fresh = NewRow(h.TenantId, (await h.AddEventAsync([], [])).Id, now.AddMinutes(-1));
        await using (var db = h.Context())
        {
            foreach (var row in new[] { stale, fresh }) { row.ExternalMeetingId = ""; row.JoinUrl = ""; }
            db.CalendarEventMeetings.AddRange(stale, fresh);
            await db.SaveChangesAsync();
        }

        await using var db2 = h.Context();
        var repo = Repo(db2);
        Assert.True(await repo.TryMarkStaleProvisioningUnknownAsync(h.TenantId, stale.Id, now.AddMinutes(-5), now));
        Assert.False(await repo.TryMarkStaleProvisioningUnknownAsync(h.TenantId, fresh.Id, now.AddMinutes(-5), now));
        Assert.True(await repo.TryClaimUnknownAsync(h.TenantId, stale.Id, now));
        Assert.False(await repo.TryClaimUnknownAsync(h.TenantId, stale.Id, now)); // already claimed
    }
}
```

Run: `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -2` → Expected: the claim methods don't exist.

- [ ] **Step 3: Implement the repository**

Add the six methods from the Interfaces block to `ICalendarEventMeetingRepository`. In `EfCalendarEventMeetingRepository`:

```csharp
    public async Task<CalendarEventMeeting?> GetByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.CalendarEventId == calendarEventId, ct);

    // The claim statements run outside any transaction, so each commits on its own and other requests
    // see the claim before this request calls the provider (spec §3.2.2).
    public async Task<bool> TryInsertProvisioningAsync(CalendarEventMeeting row, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO calendar_event_meetings
                (id, tenant_id, calendar_event_id, external_calendar_connection_id, provider, external_meeting_id, join_url,
                 status, link_generation, provisioning_started_at, created_at)
            VALUES
                ({row.Id}, {row.TenantId}, {row.CalendarEventId}, {row.ExternalCalendarConnectionId}, {row.Provider}, '', '',
                 {CalendarEventMeetingStatuses.Provisioning}, 1, {row.ProvisioningStartedAt}, {row.CreatedAt})
            ON CONFLICT (calendar_event_id) DO NOTHING
            """, ct) == 1;

    public async Task<int?> TryClaimNewGenerationAsync(
        Guid tenantId, Guid meetingId, Guid connectionId, string provider, DateTimeOffset now, CancellationToken ct = default)
    {
        var generations = await _db.Database.SqlQuery<int>($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Provisioning}, link_generation = link_generation + 1,
                provisioning_started_at = {now}, external_calendar_connection_id = {connectionId}, provider = {provider},
                external_meeting_id = '', join_url = '', organizer_join_url = NULL, passcode_or_pin = NULL,
                failure_reason = NULL, attendance_sync_status = NULL, last_attendance_synced_at = NULL, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId}
              AND status IN ({CalendarEventMeetingStatuses.Failed}, {CalendarEventMeetingStatuses.Cancelled})
            RETURNING link_generation AS "Value"
            """).ToListAsync(ct);
        return generations.Count == 1 ? generations[0] : null;
    }

    public async Task<bool> TryMarkStaleProvisioningUnknownAsync(
        Guid tenantId, Guid meetingId, DateTimeOffset staleBefore, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.ProvisioningUnknown}, failure_reason = 'timeout_or_server_error', updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId}
              AND status = {CalendarEventMeetingStatuses.Provisioning} AND provisioning_started_at < {staleBefore}
            """, ct) == 1;

    public async Task<bool> TryClaimUnknownAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Provisioning}, provisioning_started_at = {now}, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId} AND status = {CalendarEventMeetingStatuses.ProvisioningUnknown}
            """, ct) == 1;

    public async Task<bool> TryMarkUnknownUnresolvedAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Unresolved}, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId} AND status = {CalendarEventMeetingStatuses.ProvisioningUnknown}
            """, ct) == 1;
```

Check the column names against `CalendarEventMeetingConfiguration` (snake_case of the property names): `organizer_join_url`, `passcode_or_pin`, `external_calendar_connection_id`.

- [ ] **Step 4: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~MeetingClaimIntegrationTests"` → Expected: 3 passed.

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs src/ONEVO.Infrastructure/Persistence src/ONEVO.Infrastructure/Migrations src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs tests/ONEVO.Tests.Integration/Calendar/MeetingClaimIntegrationTests.cs
git commit -m "feat(calendar-meetings): durable provisioning state with atomic claims

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Link-added (keyed), link-removed and organizer notices

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Services/CalendarRecipients.cs` (keys), `ICalendarNotificationSender.cs`, `CalendarNotificationSender.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarEventInviteEmailOutboxHandler.cs` (`CalendarEmailKinds.LinkRemoved`), `EmailTemplateRenderer.cs`
- Modify: `NotificationTemplateSeeder.cs`, `NotificationTemplateSeederTests.cs`
- Modify: `CalendarNotificationSenderTests.cs`, `EmailTemplateRendererTests.cs`, and the `NotifyMeetingLinkAddedAsync` call and verify in `CreateEventMeetingCommandHandler.cs` / its tests

**Interfaces:**
- Produces:

```csharp
CalendarEmailKeys.Link(Guid meetingId, int generation, Guid rowId)        // "link:{meetingId}:{generation}:{rowId}"
CalendarEmailKeys.LinkRemoved(Guid meetingId, int generation, Guid rowId) // "link-removed:{meetingId}:{generation}:{rowId}"
CalendarEmailKinds.LinkRemoved = "link_removed"
public static class CalendarMeetingNoticeCodes { LinkFailed, LinkUnknown, Unresolved, CancelFailed } // template codes
public static class CalendarMeetingProviderNames { string Display(string provider); } // "Microsoft Teams" | "Zoom"
// ICalendarNotificationSender (changed / new)
Task NotifyMeetingLinkAddedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, string? meetingLink, CancellationToken ct = default); // email only; null link = invite without link
Task NotifyMeetingLinkRemovedAsync(Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location, IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests, string organizerName, CancellationToken ct = default);
Task NotifyOrganizerAboutMeetingAsync(Guid tenantId, Guid organizerUserId, string templateCode, string eventTitle, string providerName, CancellationToken ct = default);
```

- [ ] **Step 1: Write the failing tests**

Append to `CalendarNotificationSenderTests`:

```csharp
    [Fact]
    public async Task NotifyMeetingLinkAddedAsync_KeyedLinkEmail_NoInApp()
    {
        await BuildSut().NotifyMeetingLinkAddedAsync(
            TenantId, "Standup", Start, null, [new CalendarParticipantRecipient(EmployeeId, "link:m:1:p")], "Ada Owner", "https://zoom.us/j/1", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueKeyedAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.MeetingLink == "https://zoom.us/j/1" && p.Kind == CalendarEmailKinds.Invite),
            "link:m:1:p", TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyMeetingLinkRemovedAsync_InAppToParticipants_LinkRemovedEmailToEveryone()
    {
        await BuildSut().NotifyMeetingLinkRemovedAsync(
            TenantId, "Standup", Start, null,
            [new CalendarParticipantRecipient(EmployeeId, "link-removed:p")], [new CalendarGuestRecipient("vendor@example.com", "link-removed:g")],
            "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_meeting_link_removed", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.LinkRemoved, "link-removed:p");
        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.LinkRemoved, "link-removed:g");
    }

    [Fact]
    public async Task NotifyOrganizerAboutMeetingAsync_InAppToTheOrganizer_WithTheProviderName()
    {
        await BuildSut().NotifyOrganizerAboutMeetingAsync(
            TenantId, OrganizerUserId, CalendarMeetingNoticeCodes.LinkFailed, "Standup", "Zoom", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == OrganizerUserId && p.TemplateCode == "calendar_event_meeting_link_failed"
                && p.Placeholders["provider"] == "Zoom"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }
```

Change the existing `NotifyMeetingLinkAddedAsync_EnqueuesInviteEmailWithLinkToEachParticipant` call to pass `[new CalendarParticipantRecipient(EmployeeId)]` instead of `[EmployeeId]`, keeping the rest of its arguments.

Append to `EmailTemplateRendererTests`:

```csharp
    [Fact]
    public void RenderCalendarEventInvite_LinkRemoved_SaysSo_WithoutTheLink()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions()));

        var rendered = renderer.Render("calendar_event_invite", new
        {
            recipientName = "Ada", eventTitle = "Standup", startDateUtc = "2026-09-29T14:45:00Z",
            location = (string?)null, organizerName = "Owner", meetingLink = "https://zoom.us/j/1", kind = "link_removed"
        });

        rendered.Subject.Should().Be("Meeting link removed: Standup");
        rendered.HtmlBody.Should().NotContain("https://zoom.us/j/1");
    }
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3` → Expected: missing members.

- [ ] **Step 2: Implement**

In `CalendarRecipients.cs` add to `CalendarEmailKeys`:

```csharp
    public static string Link(Guid meetingId, int generation, Guid rowId) =>
        $"link:{meetingId}:{generation.ToString(CultureInfo.InvariantCulture)}:{rowId}";

    public static string LinkRemoved(Guid meetingId, int generation, Guid rowId) =>
        $"link-removed:{meetingId}:{generation.ToString(CultureInfo.InvariantCulture)}:{rowId}";
```

and at namespace level:

```csharp
/// <summary>In-app notices to an event's organizer about its online meeting (template codes).</summary>
public static class CalendarMeetingNoticeCodes
{
    public const string LinkFailed = "calendar_event_meeting_link_failed";
    public const string LinkUnknown = "calendar_event_meeting_link_unknown";
    public const string Unresolved = "calendar_event_meeting_unresolved";
    public const string CancelFailed = "calendar_event_meeting_cancel_failed";
}

public static class CalendarMeetingProviderNames
{
    public static string Display(string provider) =>
        provider == ONEVO.Domain.Features.Calendar.Entities.CalendarEventMeetingProviders.Zoom ? "Zoom" : "Microsoft Teams";
}
```

In `CalendarEmailKinds` add `public const string LinkRemoved = "link_removed";`. In the renderer's `kind switch`, add before the default arm:

```csharp
            "link_removed" => ($"Meeting link removed: {eventTitle}", "Online meeting removed",
                $"{Escape(organizerName)} removed the online meeting link from this event. The event itself is unchanged:",
                $"{organizerName} removed the online meeting link from \"{eventTitle}\". The event itself is unchanged.", false),
```

In `ICalendarNotificationSender`, replace `NotifyMeetingLinkAddedAsync` and add the two new methods:

```csharp
    /// <summary>Invite email (with the join link, or without one when <paramref name="meetingLink"/> is null -
    /// the P1-5 fallback) to each participant. No in-app notification (they got one when added).</summary>
    Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, string? meetingLink, CancellationToken ct = default);

    /// <summary>In-app to participants + "link removed" email to participants and guests.</summary>
    Task NotifyMeetingLinkRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default);

    /// <summary>In-app notice to the organizer about their online meeting (see CalendarMeetingNoticeCodes).</summary>
    Task NotifyOrganizerAboutMeetingAsync(
        Guid tenantId, Guid organizerUserId, string templateCode, string eventTitle, string providerName, CancellationToken ct = default);
```

In `CalendarNotificationSender`, replace `NotifyMeetingLinkAddedAsync` and add the new methods:

```csharp
    public async Task NotifyMeetingLinkAddedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, string organizerName, string? meetingLink, CancellationToken ct = default)
    {
        foreach (var recipient in participants)
        {
            var employee = await employees.GetByIdAsync(tenantId, recipient.EmployeeId, ct);
            if (employee is null) continue;

            await EnqueueEmailAsync(tenantId,
                new CalendarEventInviteEmailPayload(tenantId, employee.Email, FullName(employee), eventTitle, startDate, location, organizerName, meetingLink),
                recipient.EmailKey, ct);
        }
    }

    public Task NotifyMeetingLinkRemovedAsync(
        Guid tenantId, string eventTitle, DateTimeOffset startDate, string? location,
        IReadOnlyList<CalendarParticipantRecipient> participants, IReadOnlyList<CalendarGuestRecipient> guests,
        string organizerName, CancellationToken ct = default)
        => NotifyChangeAsync(tenantId, "calendar_event_meeting_link_removed", CalendarEmailKinds.LinkRemoved, eventTitle, startDate,
            location, meetingLink: null, participants, guests, organizerName, ct);

    public Task NotifyOrganizerAboutMeetingAsync(
        Guid tenantId, Guid organizerUserId, string templateCode, string eventTitle, string providerName, CancellationToken ct = default)
        => EnqueueInAppAsync(tenantId, organizerUserId, templateCode,
            new Dictionary<string, string> { ["eventTitle"] = eventTitle, ["provider"] = providerName }, ct);
```

Seed five templates after the 0A ones in `NotificationTemplateSeeder` (and add the five codes to the seeder test's expected list):

```csharp
            ,
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_link_removed",
                InAppTitleTemplate = "Meeting link removed",
                InAppBodyTemplate = "{{organizerName}} removed the online meeting link from \"{{eventTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_link_failed",
                InAppTitleTemplate = "Meeting link not created",
                InAppBodyTemplate = "We couldn't create the {{provider}} link for \"{{eventTitle}}\". Invitations were sent without a link; you can add a meeting from the event."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_link_unknown",
                InAppTitleTemplate = "Meeting link status unknown",
                InAppBodyTemplate = "We couldn't confirm whether the {{provider}} meeting for \"{{eventTitle}}\" was created. Open the event to retry or remove it."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_unresolved",
                InAppTitleTemplate = "A meeting may still exist",
                InAppBodyTemplate = "A {{provider}} meeting for \"{{eventTitle}}\" may still exist. Please check and delete it in {{provider}}, then confirm in the event."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "calendar_event_meeting_cancel_failed",
                InAppTitleTemplate = "Meeting not cancelled",
                InAppBodyTemplate = "We couldn't cancel the {{provider}} meeting for \"{{eventTitle}}\". Please delete it in {{provider}}."
            }
```

In `CreateEventMeetingCommandHandler.cs`, change `participantEmployeeIds, organizerName, joinUrl, innerCt);` (the `NotifyMeetingLinkAddedAsync` call) to `participantEmployeeIds.Select(id => new CalendarParticipantRecipient(id)).ToList(), organizerName, joinUrl, innerCt);`. In its test, change `It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(ParticipantEmployeeId))` to `It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Any(x => x.EmployeeId == ParticipantEmployeeId))`. These are compile-only; Task 6 rewrites both.

- [ ] **Step 3: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~Features.Calendar|FullyQualifiedName~NotificationTemplateSeederTests|FullyQualifiedName~EmailTemplateRendererTests"` → all pass.

```bash
git add src/ONEVO.Application/Features/Calendar src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs tests/ONEVO.Tests.Unit
git commit -m "feat(calendar-notifications): keyed link emails, link-removed notices and organizer meeting notices

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `MeetingProvisioner` — the provider-call policy

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Services/MeetingProvisioner.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register next to `ICalendarNotificationSender`, line ~375)
- Create: `tests/ONEVO.Tests.Unit/Features/Calendar/MeetingProvisionerTests.cs`

**Interfaces:**
- Consumes: Task 1 clients/exception, Task 2 `ForceRefreshAccessTokenAsync`, `CalendarMeetingCorrelation`.
- Produces:

```csharp
public enum MeetingProvisioningOutcomeKind { Created, Unauthorized, RateLimited, Permanent, Ambiguous, NotConnected }
public sealed record MeetingProvisioningRequest(ExternalCalendarConnection Connection, string Provider, Guid EventId, int Generation,
    string Subject, DateTimeOffset Start, DateTimeOffset End, bool Recovery);
public sealed record ProvisionedMeeting(string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);
public sealed record MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind Kind, ProvisionedMeeting? Meeting, string? Reason);
public interface IMeetingProvisioner
{
    Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct);
    /// true = the remote meeting for this generation is gone (deleted, or never existed); false = unknown.
    Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct);
}
```

Reasons: `auth_failed`, `rate_limited`, `rejected`, `not_connected`, `timeout_or_server_error`, `correlation_unavailable`.

- [ ] **Step 1: Write the failing tests**

`tests/ONEVO.Tests.Unit/Features/Calendar/MeetingProvisionerTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class MeetingProvisionerTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid() };
    private readonly Mock<ICalendarConnectionTokenProvider> _tokens = new();
    private readonly Mock<ITeamsMeetingClient> _teams = new();
    private readonly Mock<IZoomMeetingClient> _zoom = new();
    private readonly List<TimeSpan> _waits = [];

    private MeetingProvisioner BuildSut()
    {
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("t1");
        return new MeetingProvisioner(_tokens.Object, _teams.Object, _zoom.Object, NullLogger<MeetingProvisioner>.Instance,
            (wait, _) => { _waits.Add(wait); return Task.CompletedTask; });
    }

    private MeetingProvisioningRequest Request(string provider, int generation = 1, bool recovery = false) =>
        new(_connection, provider, EventId, generation, "Review", Start, Start.AddHours(1), recovery);

    private static readonly TeamsMeetingDto Teams = new("graph-1", "https://teams/j/1", null, null);
    private static readonly ZoomMeetingDto Zoom = new("999", "https://zoom.us/j/999", null, "pw");

    private void TeamsSequence(params object[] steps)
    {
        var setup = _teams.SetupSequence(t => t.CreateOrGetMeetingAsync(
            It.IsAny<string>(), $"{EventId}:1", "Review", Start, Start.AddHours(1), It.IsAny<CancellationToken>()));
        foreach (var step in steps)
            setup = step is Exception ex ? setup.ThrowsAsync(ex) : setup.ReturnsAsync((TeamsMeetingDto)step);
    }

    [Fact]
    public async Task Teams2xx_Created_UsingTheGenerationsExternalId()
    {
        TeamsSequence(Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        Assert.Equal("graph-1", outcome.Meeting!.ExternalMeetingId);
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_ThenSucceeds()
    {
        _tokens.Setup(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("t2");
        TeamsSequence(new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"), Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        _tokens.Verify(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnauthorizedTwice_IsAuthFailed()
    {
        _tokens.Setup(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("t2");
        TeamsSequence(
            new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"),
            new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"));

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Unauthorized, outcome.Kind);
        Assert.Equal("auth_failed", outcome.Reason);
    }

    [Fact]
    public async Task RateLimitedTwice_ThenCreated_HonouringRetryAfter()
    {
        TeamsSequence(
            new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromSeconds(3)),
            new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromSeconds(4)),
            Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4) }, _waits);
    }

    [Fact]
    public async Task RateLimitedThreeTimes_IsRateLimited_AfterThreeAttempts()
    {
        var limited = new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromSeconds(1));
        TeamsSequence(limited, limited, limited, Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.RateLimited, outcome.Kind);
        _teams.Verify(t => t.CreateOrGetMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RetryAfterLongerThanTheCap_WaitsOnlyTheCap()
    {
        TeamsSequence(new MeetingProviderException(MeetingProviderFailureKind.RateLimited, "429", TimeSpan.FromMinutes(5)), Teams);

        await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(MeetingProvisioner.MaxSingleWait, Assert.Single(_waits));
    }

    [Theory]
    [InlineData(MeetingProviderFailureKind.Permanent, MeetingProvisioningOutcomeKind.Permanent)]
    [InlineData(MeetingProviderFailureKind.Ambiguous, MeetingProvisioningOutcomeKind.Ambiguous)]
    public async Task OtherFailures_AreClassified_WithoutRetrying(MeetingProviderFailureKind kind, MeetingProvisioningOutcomeKind expected)
    {
        TeamsSequence(new MeetingProviderException(kind, "x"), Teams);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        Assert.Equal(expected, outcome.Kind);
    }

    [Fact]
    public async Task NoAccessToken_IsNotConnected()
    {
        var sut = BuildSut();
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var outcome = await sut.ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.NotConnected, outcome.Kind);
    }

    [Fact]
    public async Task ZoomCreate_CarriesTheGenerationsToken()
    {
        _zoom.Setup(z => z.CreateMeetingAsync("t1", "Review", Start, Start.AddHours(1), $"onexso:{EventId}:3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Zoom);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, generation: 3), CancellationToken.None);

        Assert.Equal("999", outcome.Meeting!.ExternalMeetingId);
    }

    [Fact]
    public async Task ZoomRecovery_TokenMatch_AdoptsWithoutCreating()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync("t1", $"onexso:{EventId}:1", Start, It.IsAny<CancellationToken>())).ReturnsAsync(Zoom);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, recovery: true), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
        _zoom.Verify(z => z.CreateMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ZoomRecovery_NoMatch_CreatesWithTheSameToken()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync("t1", $"onexso:{EventId}:1", Start, It.IsAny<CancellationToken>())).ReturnsAsync((ZoomMeetingDto?)null);
        _zoom.Setup(z => z.CreateMeetingAsync("t1", "Review", Start, Start.AddHours(1), $"onexso:{EventId}:1", It.IsAny<CancellationToken>())).ReturnsAsync(Zoom);

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, recovery: true), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Created, outcome.Kind);
    }

    [Fact]
    public async Task ZoomRecovery_ListUnavailable_StaysAmbiguous_AndNeverCreates()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Permanent, "403 missing scope"));

        var outcome = await BuildSut().ProvisionAsync(Request(CalendarEventMeetingProviders.Zoom, recovery: true), CancellationToken.None);

        Assert.Equal(MeetingProvisioningOutcomeKind.Ambiguous, outcome.Kind);
        Assert.Equal("correlation_unavailable", outcome.Reason);
        _zoom.Verify(z => z.CreateMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForRemoval_Teams_CreateOrGetThenDelete()
    {
        TeamsSequence(Teams);

        Assert.True(await BuildSut().ResolveForRemovalAsync(Request(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None));
        _teams.Verify(t => t.CancelMeetingAsync("t1", "graph-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveForRemoval_Zoom_NoMatchAfterAFullRead_IsResolvedWithoutADelete()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ZoomMeetingDto?)null);

        Assert.True(await BuildSut().ResolveForRemovalAsync(Request(CalendarEventMeetingProviders.Zoom), CancellationToken.None));
        _zoom.Verify(z => z.CancelMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForRemoval_Zoom_ListFails_IsUnresolved()
    {
        _zoom.Setup(z => z.FindMeetingByCorrelationTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "500"));

        Assert.False(await BuildSut().ResolveForRemovalAsync(Request(CalendarEventMeetingProviders.Zoom), CancellationToken.None));
    }
}
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -2` → Expected: `MeetingProvisioner` does not exist.

- [ ] **Step 2: Implement**

`src/ONEVO.Application/Features/Calendar/Services/MeetingProvisioner.cs`:

```csharp
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

public enum MeetingProvisioningOutcomeKind { Created, Unauthorized, RateLimited, Permanent, Ambiguous, NotConnected }

public sealed record MeetingProvisioningRequest(
    ExternalCalendarConnection Connection, string Provider, Guid EventId, int Generation,
    string Subject, DateTimeOffset Start, DateTimeOffset End, bool Recovery);

public sealed record ProvisionedMeeting(string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind Kind, ProvisionedMeeting? Meeting, string? Reason);

public interface IMeetingProvisioner
{
    /// <summary>Creates (or, in recovery, adopts) the meeting for one link generation and classifies the result.</summary>
    Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct);

    /// <summary>For Remove on provisioning_unknown: deletes the generation's remote meeting if it exists.
    /// True = nothing of it remains remotely; false = can't tell (keep the correlation data, warn the organizer).</summary>
    Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct);
}

/// <summary>Spec §3.2.2 provider-call policy: 90 s total budget; 401 → refresh once; 429 → honour
/// Retry-After, 3 attempts; timeout/5xx → ambiguous. Teams is idempotent through createOrGet; Zoom
/// recovery adopts only an exact correlation-token match.</summary>
public sealed class MeetingProvisioner(
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    ILogger<MeetingProvisioner> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IMeetingProvisioner
{
    public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan MaxSingleWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromSeconds(5);
    private const int MaxRateLimitedAttempts = 3;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);
        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        var oauthProvider = isZoom ? "zoom" : "microsoft";
        try
        {
            var accessToken = await tokenProvider.GetFreshAccessTokenAsync(request.Connection, oauthProvider, budget.Token);
            if (accessToken is null)
                return Failed(MeetingProvisioningOutcomeKind.NotConnected, "not_connected");

            if (isZoom && request.Recovery)
            {
                try
                {
                    var match = await zoomClient.FindMeetingByCorrelationTokenAsync(
                        accessToken, CalendarMeetingCorrelation.ZoomToken(request.EventId, request.Generation), request.Start, budget.Token);
                    if (match is not null)
                        return Created(match.ExternalMeetingId, match.JoinUrl, match.OrganizerJoinUrl, match.PasscodeOrPin);
                }
                catch (MeetingProviderException ex)
                {
                    // Can't tell whether the earlier attempt created one: creating now could duplicate it.
                    logger.LogWarning(ex, "Zoom correlation failed for event {EventId} generation {Generation}.", request.EventId, request.Generation);
                    return Failed(MeetingProvisioningOutcomeKind.Ambiguous, "correlation_unavailable");
                }
            }

            var refreshed = false;
            var rateLimited = 0;
            while (true)
            {
                try
                {
                    if (isZoom)
                    {
                        var zoom = await zoomClient.CreateMeetingAsync(
                            accessToken, request.Subject, request.Start, request.End,
                            CalendarMeetingCorrelation.ZoomToken(request.EventId, request.Generation), budget.Token);
                        return Created(zoom.ExternalMeetingId, zoom.JoinUrl, zoom.OrganizerJoinUrl, zoom.PasscodeOrPin);
                    }

                    var teams = await teamsClient.CreateOrGetMeetingAsync(
                        accessToken, CalendarMeetingCorrelation.TeamsExternalId(request.EventId, request.Generation),
                        request.Subject, request.Start, request.End, budget.Token);
                    return Created(teams.ExternalMeetingId, teams.JoinUrl, teams.OrganizerJoinUrl, teams.PasscodeOrPin);
                }
                catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.Unauthorized && !refreshed)
                {
                    refreshed = true;
                    accessToken = await tokenProvider.ForceRefreshAccessTokenAsync(request.Connection, oauthProvider, budget.Token);
                    if (accessToken is null)
                        return Failed(MeetingProvisioningOutcomeKind.Unauthorized, "auth_failed");
                }
                catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.RateLimited)
                {
                    if (++rateLimited >= MaxRateLimitedAttempts)
                        return Failed(MeetingProvisioningOutcomeKind.RateLimited, "rate_limited");
                    var wait = ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                        ? (retryAfter > MaxSingleWait ? MaxSingleWait : retryAfter)
                        : DefaultRateLimitWait;
                    await _delay(wait, budget.Token);
                }
                catch (MeetingProviderException ex)
                {
                    logger.LogWarning(ex, "{Provider} meeting creation failed ({Kind}) for event {EventId}.", request.Provider, ex.Kind, request.EventId);
                    return ex.Kind switch
                    {
                        MeetingProviderFailureKind.Unauthorized => Failed(MeetingProvisioningOutcomeKind.Unauthorized, "auth_failed"),
                        MeetingProviderFailureKind.Permanent => Failed(MeetingProvisioningOutcomeKind.Permanent, "rejected"),
                        _ => Failed(MeetingProvisioningOutcomeKind.Ambiguous, "timeout_or_server_error")
                    };
                }
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Failed(MeetingProvisioningOutcomeKind.Ambiguous, "timeout_or_server_error");
        }
    }

    public async Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);
        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        try
        {
            var accessToken = await tokenProvider.GetFreshAccessTokenAsync(request.Connection, isZoom ? "zoom" : "microsoft", budget.Token);
            if (accessToken is null)
                return false;

            if (isZoom)
            {
                var match = await zoomClient.FindMeetingByCorrelationTokenAsync(
                    accessToken, CalendarMeetingCorrelation.ZoomToken(request.EventId, request.Generation), request.Start, budget.Token);
                if (match is not null)
                    await zoomClient.CancelMeetingAsync(accessToken, match.ExternalMeetingId, budget.Token);
                return true; // deleted, or a complete list read found nothing to delete
            }

            // createOrGet returns the meeting if it exists (or creates it): either way we get its ID to delete.
            // No link was ever sent for this generation, so a create-then-delete is invisible to users.
            var teams = await teamsClient.CreateOrGetMeetingAsync(
                accessToken, CalendarMeetingCorrelation.TeamsExternalId(request.EventId, request.Generation),
                request.Subject, request.Start, request.End, budget.Token);
            await teamsClient.CancelMeetingAsync(accessToken, teams.ExternalMeetingId, budget.Token);
            return true;
        }
        catch (Exception ex) when (ex is MeetingProviderException or ZoomMeetingInProgressException
            || (ex is OperationCanceledException && budget.IsCancellationRequested && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Couldn't resolve the {Provider} meeting for event {EventId}; leaving it unresolved.", request.Provider, request.EventId);
            return false;
        }
    }

    private static MeetingProvisioningOutcome Created(string externalMeetingId, string joinUrl, string? organizerJoinUrl, string? passcode)
        => new(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting(externalMeetingId, joinUrl, organizerJoinUrl, passcode), null);

    private static MeetingProvisioningOutcome Failed(MeetingProvisioningOutcomeKind kind, string reason) => new(kind, null, reason);
}
```

Register in `src/ONEVO.Infrastructure/DependencyInjection.cs` next to `ICalendarNotificationSender`: `services.AddScoped<IMeetingProvisioner, MeetingProvisioner>();` (add `using ONEVO.Application.Features.Calendar.Services;` if missing).

- [ ] **Step 3: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~MeetingProvisionerTests"` → Expected: all pass.

```bash
git add src/ONEVO.Application/Features/Calendar/Services/MeetingProvisioner.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/MeetingProvisionerTests.cs
git commit -m "feat(calendar-meetings): provider-call policy with budget, 401 refresh, 429 backoff and Zoom correlation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Create, Retry, Confirm-cleanup and Get — the claim-driven flow with the P1-5 fallback

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/Services/EventMeetingLinkService.cs`
- Rewrite: `src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/RetryEventMeeting/{RetryEventMeetingCommand,RetryEventMeetingCommandHandler}.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Commands/ConfirmEventMeetingCleanup/{ConfirmEventMeetingCleanupCommand,ConfirmEventMeetingCleanupCommandHandler}.cs`
- Create: `src/ONEVO.Application/Features/Calendar/Queries/GetEventMeeting/{GetEventMeetingQuery,GetEventMeetingQueryHandler}.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register `EventMeetingLinkService`), `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs`, `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs`
- Rewrite: `tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Calendar/RetryEventMeetingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: Tasks 3–5.
- Produces:
  - `EventMeetingLinkService` with `ProvisionClaimedAsync`, `SendFallbackInvitesAsync`, `ExpireIfEndedAsync`, `ResolveUnknownForRemovalAsync` and `SendLinkRemovedAsync` (signatures below).
  - Result codes (problem `detail`): `meeting_provider_not_connected` (409), `meeting_link_in_progress` (409), `meeting_link_status_unknown` (409), `meeting_link_unresolved` (409), `meeting_link_failed` (502), `meeting_link_not_retryable` (409), `meeting_link_not_unresolved` (409).
  - Endpoints: `GET {id}/meeting` → `{ status, provider, joinUrl, failureReason }` (`status` is `"none"` when there is no row); `POST {id}/meeting/retry` → `{ joinUrl }`; `POST {id}/meeting/confirm-cleanup` → 204.

- [ ] **Step 1: Write the service**

`src/ONEVO.Application/Features/Calendar/Services/EventMeetingLinkService.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Everything that happens after a meeting row is claimed: the provider call, the row's next
/// state, and who gets told what (spec §3.2.2, P1-5). Stateless; scoped like its dependencies.</summary>
public sealed class EventMeetingLinkService(
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    IExternalCalendarConnectionRepository connections,
    IMeetingProvisioner provisioner,
    ICalendarNotificationSender notifications,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public async Task<Result<CreateEventMeetingResult>> ProvisionClaimedAsync(
        CalendarEvent evt, CalendarEventMeeting claimed, ExternalCalendarConnection connection, bool recovery,
        string organizerName, CancellationToken ct)
    {
        var outcome = await provisioner.ProvisionAsync(new MeetingProvisioningRequest(
            connection, claimed.Provider, evt.Id, claimed.LinkGeneration, evt.Title, evt.StartDate, evt.EndDate, recovery), ct);
        var providerName = CalendarMeetingProviderNames.Display(claimed.Provider);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            claimed.UpdatedAt = clock.UtcNow;
            if (outcome.Kind == MeetingProvisioningOutcomeKind.Created)
            {
                var meeting = outcome.Meeting!;
                claimed.Status = CalendarEventMeetingStatuses.Active;
                claimed.ExternalMeetingId = meeting.ExternalMeetingId;
                claimed.JoinUrl = meeting.JoinUrl;
                claimed.OrganizerJoinUrl = meeting.OrganizerJoinUrl;
                claimed.PasscodeOrPin = meeting.PasscodeOrPin;
                claimed.FailureReason = null;
                claimed.ProvisioningStartedAt = null;
                meetings.Update(claimed);
                evt.MeetingLink = meeting.JoinUrl;
                events.Update(evt);
                await SendLinkEmailsAsync(evt, claimed, organizerName, innerCt);
                await unitOfWork.SaveChangesAsync(innerCt);
                return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(meeting.JoinUrl));
            }

            var ambiguous = outcome.Kind == MeetingProvisioningOutcomeKind.Ambiguous;
            claimed.Status = ambiguous ? CalendarEventMeetingStatuses.ProvisioningUnknown : CalendarEventMeetingStatuses.Failed;
            claimed.FailureReason = outcome.Reason;
            if (!ambiguous)
                claimed.ProvisioningStartedAt = null;
            meetings.Update(claimed);
            // P1-5: nobody may be left uninvited because the link didn't happen.
            await SendFallbackInvitesAsync(evt, organizerName, innerCt);
            await notifications.NotifyOrganizerAboutMeetingAsync(
                evt.TenantId, evt.CreatedById,
                ambiguous ? CalendarMeetingNoticeCodes.LinkUnknown : CalendarMeetingNoticeCodes.LinkFailed,
                evt.Title, providerName, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);

            return outcome.Kind switch
            {
                MeetingProvisioningOutcomeKind.Ambiguous => Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown"),
                MeetingProvisioningOutcomeKind.NotConnected => Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected"),
                _ => Result<CreateEventMeetingResult>.Failure("meeting_link_failed", 502)
            };
        }, ct);
    }

    /// <summary>Invite emails WITHOUT a link to every participant and guest, keyed invite:{rowId} - the
    /// same key as the original invite, so anyone already invited is not invited again. Needs the
    /// caller's transaction (keyed emails).</summary>
    public async Task SendFallbackInvitesAsync(CalendarEvent evt, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count > 0)
            await notifications.NotifyMeetingLinkAddedAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Invite(p.Id))).ToList(),
                organizerName, meetingLink: null, ct);
        if (guests.Count > 0)
            await notifications.NotifyGuestsAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Invite(g.Id))).ToList(),
                organizerName, meetingLink: null, ct);
    }

    /// <summary>In-app notice to the event's organizer (see CalendarMeetingNoticeCodes).</summary>
    public Task NotifyOrganizerAsync(CalendarEvent evt, string noticeCode, string provider, CancellationToken ct)
        => notifications.NotifyOrganizerAboutMeetingAsync(
            evt.TenantId, evt.CreatedById, noticeCode, evt.Title, CalendarMeetingProviderNames.Display(provider), ct);

    /// <summary>Spec: an event that ended while provisioning_unknown becomes unresolved when next read
    /// (the stray-meeting data is kept and the organizer is warned). Returns the row as it now stands.</summary>
    public async Task<CalendarEventMeeting> ExpireIfEndedAsync(CalendarEvent evt, CalendarEventMeeting snapshot, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (snapshot.Status != CalendarEventMeetingStatuses.ProvisioningUnknown || evt.EndDate >= now)
            return snapshot;
        if (!await meetings.TryMarkUnknownUnresolvedAsync(evt.TenantId, snapshot.Id, now, ct))
            return await meetings.GetByCalendarEventAsync(evt.TenantId, evt.Id, ct) ?? snapshot;

        await notifications.NotifyOrganizerAboutMeetingAsync(
            evt.TenantId, evt.CreatedById, CalendarMeetingNoticeCodes.Unresolved, evt.Title,
            CalendarMeetingProviderNames.Display(snapshot.Provider), ct);
        await unitOfWork.SaveChangesAsync(ct);
        snapshot.Status = CalendarEventMeetingStatuses.Unresolved;
        return snapshot;
    }

    /// <summary>Remove on provisioning_unknown: resolve the remote side first (spec §3.2.2).</summary>
    public async Task<Result> ResolveUnknownForRemovalAsync(CalendarEvent evt, CalendarEventMeeting snapshot, CancellationToken ct)
    {
        if (!await meetings.TryClaimUnknownAsync(evt.TenantId, snapshot.Id, clock.UtcNow, ct))
            return Result.Conflict("meeting_link_in_progress");

        var meeting = (await meetings.GetTrackedByCalendarEventAsync(evt.TenantId, evt.Id, ct))!;
        var connection = await connections.GetByIdForTenantAsync(evt.TenantId, meeting.ExternalCalendarConnectionId, ct);
        var resolved = connection is not null && await provisioner.ResolveForRemovalAsync(new MeetingProvisioningRequest(
            connection, meeting.Provider, evt.Id, meeting.LinkGeneration, evt.Title, evt.StartDate, evt.EndDate, Recovery: true), ct);

        meeting.Status = resolved ? CalendarEventMeetingStatuses.Cancelled : CalendarEventMeetingStatuses.Unresolved;
        meeting.ProvisioningStartedAt = null;
        meeting.UpdatedAt = clock.UtcNow;
        meetings.Update(meeting);
        if (!resolved)
            await notifications.NotifyOrganizerAboutMeetingAsync(
                evt.TenantId, evt.CreatedById, CalendarMeetingNoticeCodes.Unresolved, evt.Title,
                CalendarMeetingProviderNames.Display(meeting.Provider), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return resolved ? Result.Success() : Result.Conflict("meeting_link_unresolved");
    }

    /// <summary>P1-4: the link went away - in-app to participants, email to participants and guests,
    /// once per generation. Needs the caller's transaction.</summary>
    public async Task SendLinkRemovedAsync(CalendarEvent evt, CalendarEventMeeting meeting, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count == 0 && guests.Count == 0)
            return;
        await notifications.NotifyMeetingLinkRemovedAsync(
            evt.TenantId, evt.Title, evt.StartDate, evt.Location,
            participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.LinkRemoved(meeting.Id, meeting.LinkGeneration, p.Id))).ToList(),
            guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.LinkRemoved(meeting.Id, meeting.LinkGeneration, g.Id))).ToList(),
            organizerName, ct);
    }

    private async Task SendLinkEmailsAsync(CalendarEvent evt, CalendarEventMeeting meeting, string organizerName, CancellationToken ct)
    {
        var (participants, guests) = await AttendeesAsync(evt, ct);
        if (participants.Count > 0)
            await notifications.NotifyMeetingLinkAddedAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                participants.Select(p => new CalendarParticipantRecipient(p.EmployeeId, CalendarEmailKeys.Link(meeting.Id, meeting.LinkGeneration, p.Id))).ToList(),
                organizerName, meeting.JoinUrl, ct);
        if (guests.Count > 0)
            await notifications.NotifyGuestsAsync(
                evt.TenantId, evt.Title, evt.StartDate, evt.Location,
                guests.Select(g => new CalendarGuestRecipient(g.Email, CalendarEmailKeys.Link(meeting.Id, meeting.LinkGeneration, g.Id))).ToList(),
                organizerName, meeting.JoinUrl, ct);
    }

    private async Task<(IReadOnlyList<CalendarEventParticipant> Participants, IReadOnlyList<CalendarEventGuest> Guests)> AttendeesAsync(
        CalendarEvent evt, CancellationToken ct)
        => ((await events.GetParticipantsForEventsAsync(evt.TenantId, [evt.Id], ct)).GetValueOrDefault(evt.Id) ?? [],
            (await events.GetGuestsForEventsAsync(evt.TenantId, [evt.Id], ct)).GetValueOrDefault(evt.Id) ?? []);
}
```

Register in `src/ONEVO.Infrastructure/DependencyInjection.cs`: `services.AddScoped<EventMeetingLinkService>();`.

- [ ] **Step 2: Rewrite the create handler tests (failing)**

Replace `tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs` with:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly CalendarEvent _evt = new()
    {
        Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Review",
        StartDate = Now.AddDays(1), EndDate = Now.AddDays(1).AddHours(1)
    };

    private readonly ExternalCalendarConnection _connection = new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.OutlookCalendar,
        Status = ExternalCalendarConnectionStatuses.Active, ScopesJson = """["OnlineMeetings.ReadWrite"]"""
    };

    private CreateEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.OutlookCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_connection);
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>, CancellationToken>((op, ct) => op(ct));
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>((op, ct) => op(ct));
        var links = new EventMeetingLinkService(_events.Object, _meetings.Object, _connections.Object, _provisioner.Object,
            _notifications.Object, _unitOfWork.Object, _clock.Object);
        return new CreateEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _connections.Object, _meetings.Object, links, _employees.Object, _clock.Object, _unitOfWork.Object);
    }

    private void FirstClaimSucceeds(int generation = 1)
    {
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _meetings.Setup(m => m.TryInsertProvisioningAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
                Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = generation, ExternalCalendarConnectionId = _connection.Id
            });
    }

    private void ProvisionerReturns(MeetingProvisioningOutcome outcome) =>
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

    private static CreateEventMeetingCommand Teams() => new(EventId, CalendarEventMeetingProviders.MicrosoftTeams);

    [Fact]
    public async Task Created_SetsTheLink_AndKeysLinkEmailsOnTheGeneration()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("graph-1", "https://teams/j/1", null, null), null));
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://teams/j/1", _evt.MeetingLink);
        _notifications.Verify(n => n.NotifyMeetingLinkAddedAsync(
            TenantId, "Review", _evt.StartDate, null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey!.StartsWith("link:") && r.Single().EmailKey!.Contains($":1:{participant.Id}")),
            "Ada Owner", "https://teams/j/1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActiveMeetingAlready_ReturnsItsLink_WithoutAProviderCall()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, JoinUrl = "https://teams/j/old" });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("https://teams/j/old", result.Value!.JoinUrl);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LostTheFirstClaim_Is409InProgress_WithoutAProviderCall()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _meetings.Setup(m => m.TryInsertProvisioningAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("meeting_link_in_progress", result.Error);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FreshProvisioning_Is409InProgress()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Provisioning, ProvisioningStartedAt = Now.AddMinutes(-1) });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_link_in_progress", result.Error);
    }

    [Fact]
    public async Task StaleProvisioning_BecomesUnknown_FallbackInvitesGoOut_AndIs409Unknown()
    {
        var sut = BuildSut();
        var stale = new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Provisioning, ProvisioningStartedAt = Now.AddMinutes(-6), Provider = CalendarEventMeetingProviders.MicrosoftTeams };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        _meetings.Setup(m => m.TryMarkStaleProvisioningUnknownAsync(TenantId, stale.Id, Now.AddMinutes(-5), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_link_status_unknown", result.Error);
        _notifications.Verify(n => n.NotifyMeetingLinkAddedAsync(
            TenantId, "Review", _evt.StartDate, null,
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"invite:{participant.Id}"),
            "Ada Owner", null, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkUnknown, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(CalendarEventMeetingStatuses.ProvisioningUnknown, "meeting_link_status_unknown")]
    [InlineData(CalendarEventMeetingStatuses.Unresolved, "meeting_link_unresolved")]
    public async Task UnknownOrUnresolved_RefusesWithoutAProviderCall(string status, string error)
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = status });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(error, result.Error);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AfterFailedOrCancelled_ClaimsTheNextGeneration()
    {
        var sut = BuildSut();
        var failed = new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Failed, LinkGeneration = 2 };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(failed);
        _meetings.Setup(m => m.TryClaimNewGenerationAsync(TenantId, failed.Id, _connection.Id, CalendarEventMeetingProviders.MicrosoftTeams, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = failed.Id, Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = 3, Provider = CalendarEventMeetingProviders.MicrosoftTeams });
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/3", null, null), null));

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _provisioner.Verify(p => p.ProvisionAsync(It.Is<MeetingProvisioningRequest>(r => r.Generation == 3 && !r.Recovery), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderRejects_IsFailed502_InvitesWithoutLink_OrganizerNoticed()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Permanent, null, "rejected"));
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guest] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(502, result.StatusCode);
        Assert.Equal("meeting_link_failed", result.Error);
        _notifications.Verify(n => n.NotifyGuestsAsync(
            TenantId, "Review", _evt.StartDate, null,
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"invite:{guest.Id}"), "Ada Owner", null, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkFailed, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ambiguous_IsProvisioningUnknown409_WithFallbackAndNotice()
    {
        var sut = BuildSut();
        FirstClaimSucceeds();
        ProvisionerReturns(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Ambiguous, null, "timeout_or_server_error"));

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_link_status_unknown", result.Error);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.LinkUnknown, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
        _meetings.Verify(m => m.Update(It.Is<CalendarEventMeeting>(x => x.Status == CalendarEventMeetingStatuses.ProvisioningUnknown)), Times.Once);
    }

    [Fact]
    public async Task NotConnected_SendsTheHeldBackInvites_And409()
    {
        var sut = BuildSut();
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventMeeting?)null);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.OutlookCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = Guid.NewGuid() };
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [EventId] = [participant] });

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal("meeting_provider_not_connected", result.Error);
        _notifications.Verify(n => n.NotifyMeetingLinkAddedAsync(
            TenantId, "Review", _evt.StartDate, null, It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), "Ada Owner", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotOrganizer_Forbidden()
    {
        var sut = BuildSut();
        _evt.CreatedById = Guid.NewGuid();

        var result = await sut.Handle(Teams(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3` → Expected: constructor mismatch.

- [ ] **Step 3: Rewrite the create handler**

Replace `CreateEventMeetingCommandHandler.cs` with:

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

/// <summary>Claims the event's meeting row atomically, then provisions (spec §3.2.2). A request that
/// doesn't win the claim makes no provider call.</summary>
public sealed class CreateEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarEventMeetingRepository meetings,
    EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    IDateTimeProvider clock,
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

        var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
        var now = clock.UtcNow;

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, existing.Id, ct);
        if (snapshot is not null)
        {
            snapshot = await links.ExpireIfEndedAsync(existing, snapshot, ct);
            switch (snapshot.Status)
            {
                case CalendarEventMeetingStatuses.Active:
                    return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(snapshot.JoinUrl));
                case CalendarEventMeetingStatuses.ProvisioningUnknown:
                    return Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown");
                case CalendarEventMeetingStatuses.Unresolved:
                    return Result<CreateEventMeetingResult>.Conflict("meeting_link_unresolved");
                case CalendarEventMeetingStatuses.Provisioning:
                    // Someone else's request owns the row. Only once it is stale (its request crashed, or its
                    // save failed after the provider call) does it become provisioning_unknown, for Retry/Remove.
                    if (await meetings.TryMarkStaleProvisioningUnknownAsync(tenantId, snapshot.Id, now - EventMeetingLinkService.StaleAfter, now, ct))
                    {
                        await SendFallbackAsync(existing, organizerName, ct, CalendarMeetingNoticeCodes.LinkUnknown, snapshot.Provider);
                        return Result<CreateEventMeetingResult>.Conflict("meeting_link_status_unknown");
                    }
                    return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");
            }
        }

        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        var connection = await connections.GetByTenantUserProviderAsync(
            tenantId, currentUser.UserId, isZoom ? CalendarExternalSources.Zoom : CalendarExternalSources.OutlookCalendar, ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson, isZoom ? "meeting:write:meeting" : "OnlineMeetings.ReadWrite"))
        {
            // P1-5: the one-click create flow held invites back for the link; send them without one.
            await SendFallbackAsync(existing, organizerName, ct);
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");
        }

        if (snapshot is null)
        {
            var row = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id, ExternalCalendarConnectionId = connection.Id,
                Provider = request.Provider, Status = CalendarEventMeetingStatuses.Provisioning, LinkGeneration = 1,
                ProvisioningStartedAt = now, CreatedAt = now
            };
            if (!await meetings.TryInsertProvisioningAsync(row, ct))
                return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");
        }
        else if (await meetings.TryClaimNewGenerationAsync(tenantId, snapshot.Id, connection.Id, request.Provider, now, ct) is null)
        {
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");
        }

        var claimed = (await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, ct))!;
        return await links.ProvisionClaimedAsync(existing, claimed, connection, recovery: false, organizerName, ct);
    }

    /// <summary>P1-5 fallback in its own transaction (keyed emails need one); with a notice code the
    /// organizer is also told why there is no link.</summary>
    private Task SendFallbackAsync(
        CalendarEvent evt, string organizerName, CancellationToken ct, string? noticeCode = null, string? provider = null)
        => unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await links.SendFallbackInvitesAsync(evt, organizerName, innerCt);
            if (noticeCode is not null)
                await links.NotifyOrganizerAsync(evt, noticeCode, provider!, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

    private static bool HasMeetingScope(string scopesJson, string requiredScope)
    {
        try
        {
            var scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? [];
            return scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Retry, confirm-cleanup and get**

`src/ONEVO.Application/Features/Calendar/Commands/RetryEventMeeting/RetryEventMeetingCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

namespace ONEVO.Application.Features.Calendar.Commands.RetryEventMeeting;

/// <summary>The organizer's Retry on a provisioning_unknown meeting: same generation, adopt-first.</summary>
public sealed record RetryEventMeetingCommand(Guid EventId) : IRequest<Result<CreateEventMeetingResult>>;
```

`RetryEventMeetingCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.RetryEventMeeting;

public sealed class RetryEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    IExternalCalendarConnectionRepository connections,
    ICalendarEventMeetingRepository meetings,
    EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    IDateTimeProvider clock)
    : IRequestHandler<RetryEventMeetingCommand, Result<CreateEventMeetingResult>>
{
    public async Task<Result<CreateEventMeetingResult>> Handle(RetryEventMeetingCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<CreateEventMeetingResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var evt = await events.GetTrackedByIdForTenantAsync(tenantId, request.EventId, ct);
        if (evt is null)
            return Result<CreateEventMeetingResult>.NotFound("Calendar event not found.");
        if (evt.CreatedById != currentUser.UserId)
            return Result<CreateEventMeetingResult>.Forbidden("Only the event organizer can retry its meeting.");

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, evt.Id, ct);
        if (snapshot is null)
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_not_retryable");
        snapshot = await links.ExpireIfEndedAsync(evt, snapshot, ct);
        if (snapshot.Status == CalendarEventMeetingStatuses.Unresolved)
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_unresolved");
        if (snapshot.Status != CalendarEventMeetingStatuses.ProvisioningUnknown)
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_not_retryable");

        // Recovery must use the connection that made the first attempt: createOrGet / the Zoom token are per organizer account.
        var connection = await connections.GetByIdForTenantAsync(tenantId, snapshot.ExternalCalendarConnectionId, ct);
        if (connection is null || connection.Status != ExternalCalendarConnectionStatuses.Active)
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");

        if (!await meetings.TryClaimUnknownAsync(tenantId, snapshot.Id, clock.UtcNow, ct))
            return Result<CreateEventMeetingResult>.Conflict("meeting_link_in_progress");

        var organizer = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
        var claimed = (await meetings.GetTrackedByCalendarEventAsync(tenantId, evt.Id, ct))!;
        return await links.ProvisionClaimedAsync(evt, claimed, connection, recovery: true, organizerName, ct);
    }
}
```

`src/ONEVO.Application/Features/Calendar/Commands/ConfirmEventMeetingCleanup/ConfirmEventMeetingCleanupCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.ConfirmEventMeetingCleanup;

/// <summary>"I've deleted it in Teams/Zoom": the only way out of unresolved (spec §3.2.2).</summary>
public sealed record ConfirmEventMeetingCleanupCommand(Guid EventId) : IRequest<Result>;
```

`ConfirmEventMeetingCleanupCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.ConfirmEventMeetingCleanup;

public sealed class ConfirmEventMeetingCleanupCommandHandler(
    ICurrentUser currentUser, ICalendarEventRepository events, ICalendarEventMeetingRepository meetings,
    IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<ConfirmEventMeetingCleanupCommand, Result>
{
    public async Task<Result> Handle(ConfirmEventMeetingCleanupCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Forbidden();

        var tenantId = currentUser.TenantId;
        var evt = await events.GetByIdForTenantAsync(tenantId, request.EventId, ct);
        if (evt is null)
            return Result.NotFound("Calendar event not found.");
        if (evt.CreatedById != currentUser.UserId)
            return Result.Forbidden("Only the event organizer can confirm this.");

        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, evt.Id, ct);
        if (meeting is null || meeting.Status != CalendarEventMeetingStatuses.Unresolved)
            return Result.Conflict("meeting_link_not_unresolved");

        meeting.Status = CalendarEventMeetingStatuses.Cancelled;
        meeting.UpdatedAt = clock.UtcNow;
        meetings.Update(meeting);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

`src/ONEVO.Application/Features/Calendar/Queries/GetEventMeeting/GetEventMeetingQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeeting;

/// <summary><see cref="Status"/> is "none" when the event has no online meeting row.</summary>
public sealed record EventMeetingStatusResult(string Status, string? Provider, string? JoinUrl, string? FailureReason);

public sealed record GetEventMeetingQuery(Guid EventId) : IRequest<Result<EventMeetingStatusResult>>;
```

`GetEventMeetingQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeeting;

public sealed class GetEventMeetingQueryHandler(
    ICurrentUser currentUser, ICalendarEventRepository events, ICalendarEventMeetingRepository meetings, EventMeetingLinkService links)
    : IRequestHandler<GetEventMeetingQuery, Result<EventMeetingStatusResult>>
{
    public async Task<Result<EventMeetingStatusResult>> Handle(GetEventMeetingQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EventMeetingStatusResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var evt = await events.GetByIdForTenantAsync(tenantId, request.EventId, ct);
        if (evt is null)
            return Result<EventMeetingStatusResult>.NotFound("Calendar event not found.");
        if (evt.CreatedById != currentUser.UserId)
            return Result<EventMeetingStatusResult>.Forbidden("Only the event organizer can see its meeting status.");

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, evt.Id, ct);
        if (snapshot is null)
            return Result<EventMeetingStatusResult>.Success(new EventMeetingStatusResult("none", null, null, null));

        snapshot = await links.ExpireIfEndedAsync(evt, snapshot, ct);
        return Result<EventMeetingStatusResult>.Success(new EventMeetingStatusResult(
            snapshot.Status, snapshot.Provider,
            snapshot.Status == CalendarEventMeetingStatuses.Active ? snapshot.JoinUrl : null,
            snapshot.FailureReason));
    }
}
```

`EventMeetingLinkService.ExpireIfEndedAsync` is given an event loaded with `GetByIdForTenantAsync` (untracked); it only reads `TenantId`, `Id`, `EndDate`, `CreatedById`, `Title`.

In `CalendarContracts.cs` add:

```csharp
public sealed record EventMeetingStatusViewModel(string Status, string? Provider, string? JoinUrl, string? FailureReason);
```

In `CalendarController`, add usings for the three new namespaces and these actions after `RemoveMeeting`:

```csharp
    [HttpGet("{id:guid}/meeting")]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> GetMeeting(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEventMeetingQuery(id), ct);
        return result.IsSuccess
            ? Ok(new EventMeetingStatusViewModel(result.Value!.Status, result.Value.Provider, result.Value.JoinUrl, result.Value.FailureReason))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/meeting/retry")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> RetryMeeting(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RetryEventMeetingCommand(id), ct);
        return result.IsSuccess
            ? Ok(new CreateEventMeetingResponseModel(result.Value!.JoinUrl))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/meeting/confirm-cleanup")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> ConfirmMeetingCleanup(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmEventMeetingCleanupCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 5: Retry tests**

`tests/ONEVO.Tests.Unit/Features/Calendar/RetryEventMeetingCommandHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.Commands.RetryEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RetryEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly CalendarEvent _evt = new() { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Review", StartDate = Now.AddDays(1), EndDate = Now.AddDays(1).AddHours(1) };
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active };

    private RetryEventMeetingCommandHandler BuildSut(string status)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        var row = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Status = status, LinkGeneration = 2,
            Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalCalendarConnectionId = _connection.Id
        };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>, CancellationToken>((op, ct) => op(ct));
        var links = new EventMeetingLinkService(_events.Object, _meetings.Object, _connections.Object, _provisioner.Object,
            Mock.Of<ICalendarNotificationSender>(), _unitOfWork.Object, _clock.Object);
        return new RetryEventMeetingCommandHandler(_currentUser.Object, _events.Object, _connections.Object, _meetings.Object, links,
            Mock.Of<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>(), _clock.Object);
    }

    [Fact]
    public async Task Unknown_RecoversTheSameGeneration()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown);
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/2", null, null), null));

        var result = await sut.Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _provisioner.Verify(p => p.ProvisionAsync(It.Is<MeetingProvisioningRequest>(r => r.Recovery && r.Generation == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CalendarEventMeetingStatuses.Active)]
    [InlineData(CalendarEventMeetingStatuses.Failed)]
    public async Task NotUnknown_IsNotRetryable(string status)
    {
        var result = await BuildSut(status).Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal("meeting_link_not_retryable", result.Error);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownButTheEventEnded_IsUnresolved_NotRetried()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown);
        _evt.EndDate = Now.AddHours(-1);
        _meetings.Setup(m => m.TryMarkUnknownUnresolvedAsync(TenantId, It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await sut.Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal("meeting_link_unresolved", result.Error);
    }
}
```

- [ ] **Step 6: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~CreateEventMeetingCommandHandlerTests|FullyQualifiedName~RetryEventMeetingCommandHandlerTests"` → all pass. `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo` → 0 errors. `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -nologo` → `Failed: 0`. If an architecture rule rejects a public service class in `Services/` without an interface, follow the rule's message: add `IEventMeetingLinkService` with the same members and register the interface.

```bash
git add src/ONEVO.Application/Features/Calendar src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Api tests/ONEVO.Tests.Unit/Features/Calendar
git commit -m "feat(calendar-meetings): claim-driven create, retry, confirm-cleanup and status endpoints with the P1-5 fallback

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Remove meeting — per-status behaviour and link-removed notices (P1-4)

**Files:**
- Rewrite: `src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommandHandler.cs`
- Rewrite: `tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `EventMeetingLinkService.ResolveUnknownForRemovalAsync`, `SendLinkRemovedAsync`, `ExpireIfEndedAsync` (Task 6).

- [ ] **Step 1: Write the failing tests**

Replace the test file with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RemoveEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokens = new();
    private readonly Mock<ITeamsMeetingClient> _teams = new();
    private readonly Mock<IZoomMeetingClient> _zoom = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly CalendarEvent _evt = new()
    {
        Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Review", MeetingLink = "https://teams/j/1",
        StartDate = Now.AddDays(1), EndDate = Now.AddDays(1).AddHours(1)
    };
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active };
    private CalendarEventMeeting _row = null!;

    private RemoveEventMeetingCommandHandler BuildSut(string? status, string provider = CalendarEventMeetingProviders.MicrosoftTeams)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _row = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Provider = provider, Status = status ?? "",
            ExternalMeetingId = "remote-1", JoinUrl = "https://teams/j/1", LinkGeneration = 2, ExternalCalendarConnectionId = _connection.Id
        };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(status is null ? null : _row);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(status is null ? null : _row);
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((op, ct) => op(ct));
        var employees = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        var links = new EventMeetingLinkService(_events.Object, _meetings.Object, _connections.Object, _provisioner.Object,
            _notifications.Object, _unitOfWork.Object, _clock.Object);
        return new RemoveEventMeetingCommandHandler(_currentUser.Object, _events.Object, _meetings.Object, _connections.Object,
            _tokens.Object, _teams.Object, _zoom.Object, links, employees.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task NoRow_ManualLink_IsClearedLocally()
    {
        var result = await BuildSut(status: null).Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(_evt.MeetingLink);
    }

    [Fact]
    public async Task Active_CancelsRemotely_ClearsTheLink_AndTellsEveryoneOncePerGeneration()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active);
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guest] });

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _teams.Verify(t => t.CancelMeetingAsync("token", "remote-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, _row.Status);
        Assert.Null(_evt.MeetingLink);
        _notifications.Verify(n => n.NotifyMeetingLinkRemovedAsync(
            TenantId, "Review", _evt.StartDate, null, It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"link-removed:{_row.Id}:2:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Active_ZoomInProgress_Is409_AndLeavesEverything()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active, CalendarEventMeetingProviders.Zoom);
        _zoom.Setup(z => z.CancelMeetingAsync("token", "remote-1", It.IsAny<CancellationToken>())).ThrowsAsync(new ZoomMeetingInProgressException());

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(CalendarEventMeetingStatuses.Active, _row.Status);
        Assert.Equal("https://teams/j/1", _evt.MeetingLink);
    }

    [Fact]
    public async Task Active_ProviderUnavailable_Is502_AndLeavesEverything()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active);
        _teams.Setup(t => t.CancelMeetingAsync("token", "remote-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "503"));

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal(502, result.StatusCode);
        Assert.Equal(CalendarEventMeetingStatuses.Active, _row.Status);
    }

    [Fact]
    public async Task Unknown_ResolvedRemotely_IsCancelled()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, _row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _provisioner.Setup(p => p.ResolveForRemovalAsync(It.Is<MeetingProvisioningRequest>(r => r.Generation == 2), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, _row.Status);
    }

    [Fact]
    public async Task Unknown_CantResolve_IsUnresolved_AndTheOrganizerIsWarned()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown, CalendarEventMeetingProviders.Zoom);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, _row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _provisioner.Setup(p => p.ResolveForRemovalAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal("meeting_link_unresolved", result.Error);
        Assert.Equal(CalendarEventMeetingStatuses.Unresolved, _row.Status);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.Unresolved, "Review", "Zoom", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CalendarEventMeetingStatuses.Provisioning, "meeting_link_in_progress")]
    [InlineData(CalendarEventMeetingStatuses.Unresolved, "meeting_link_unresolved")]
    public async Task InProgressOrUnresolved_Refuses(string status, string error)
    {
        var result = await BuildSut(status).Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task NotOrganizer_Forbidden()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active);
        _evt.CreatedById = Guid.NewGuid();

        Assert.Equal(403, (await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None)).StatusCode);
    }
}
```

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -2` → Expected: constructor mismatch.

- [ ] **Step 2: Rewrite the handler**

Replace the class in `RemoveEventMeetingCommandHandler.cs` (keep the file's usings, and add `using ONEVO.Application.Features.Calendar.Services;`):

```csharp
public sealed class RemoveEventMeetingCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    EventMeetingLinkService links,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
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

        var snapshot = await meetings.GetByCalendarEventAsync(tenantId, request.EventId, ct);
        if (snapshot is null)
            return await ClearLocalLinkAsync(existing, ct); // a pasted link: nothing tracked remotely

        snapshot = await links.ExpireIfEndedAsync(existing, snapshot, ct);
        return snapshot.Status switch
        {
            CalendarEventMeetingStatuses.Active => await RemoveActiveAsync(existing, ct),
            CalendarEventMeetingStatuses.ProvisioningUnknown => await links.ResolveUnknownForRemovalAsync(existing, snapshot, ct),
            CalendarEventMeetingStatuses.Provisioning => Result.Conflict("meeting_link_in_progress"),
            CalendarEventMeetingStatuses.Unresolved => Result.Conflict("meeting_link_unresolved"),
            _ => await ClearLocalLinkAsync(existing, ct) // failed / cancelled: nothing exists remotely
        };
    }

    private async Task<Result> RemoveActiveAsync(CalendarEvent existing, CancellationToken ct)
    {
        var meeting = (await meetings.GetTrackedByCalendarEventAsync(existing.TenantId, existing.Id, ct))!;
        try
        {
            await CancelRemoteMeetingAsync(existing.TenantId, meeting, ct);
        }
        catch (ZoomMeetingInProgressException ex)
        {
            return Result.Conflict($"{ex.Message} Try again once the meeting has ended.");
        }
        catch (MeetingProviderException ex)
        {
            var provider = CalendarMeetingProviderNames.Display(meeting.Provider);
            return Result.Failure($"We couldn't remove the meeting in {provider} ({ex.Kind}). Try again.", 502);
        }

        var organizer = await employees.GetDefaultForUserAsync(existing.TenantId, currentUser.UserId, ct);
        var organizerName = organizer is null ? "Someone" : $"{organizer.FirstName} {organizer.LastName}";
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            meeting.Status = CalendarEventMeetingStatuses.Cancelled;
            meetings.Update(meeting);
            existing.MeetingLink = null;
            events.Update(existing);
            // P1-4: people must not keep a dead join link.
            await links.SendLinkRemovedAsync(existing, meeting, organizerName, innerCt);
            await unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }

    private async Task<Result> ClearLocalLinkAsync(CalendarEvent existing, CancellationToken ct)
    {
        if (existing.MeetingLink is not null)
        {
            existing.MeetingLink = null;
            events.Update(existing);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    private async Task CancelRemoteMeetingAsync(Guid tenantId, CalendarEventMeeting meeting, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return; // connection disconnected since: nothing we can cancel with

        var isZoom = meeting.Provider == CalendarEventMeetingProviders.Zoom;
        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, isZoom ? "zoom" : "microsoft", ct);
        if (accessToken is null)
            return; // re-auth required: the local link is still cleared

        if (isZoom)
            await zoomClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
        else
            await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
    }
}
```

- [ ] **Step 3: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~RemoveEventMeetingCommandHandlerTests"` → all pass.

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs
git commit -m "feat(calendar-meetings): remove resolves unknown meetings first and tells people the link is gone

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: X-2 — delete locally first; cancel remotely only after the commit

**Files:**
- Create: `src/ONEVO.Application/Common/Exceptions/OutboxPermanentFailureException.cs`
- Modify: `src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxProcessor.cs:129-151`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs` (`OutboxMessageTypes.CalendarMeetingCancel`)
- Create: `src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarMeetingCancelOutboxHandler.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs:114` (register)
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/DeleteCalendarEvent/DeleteCalendarEventCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Calendar/DeleteCalendarEventCommandHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarMeetingCancelOutboxHandlerTests.cs`, `tests/ONEVO.Tests.Unit/Features/SharedPlatform/OutboxProcessorFailureTests.cs`

**Interfaces:**
- Produces: `public sealed record CalendarMeetingCancelPayload(Guid TenantId, string Provider, string ExternalMeetingId, Guid ExternalCalendarConnectionId, Guid OrganizerUserId, Guid OrganizerEmployeeId, string EventTitle);`. `OutboxMessageTypes.CalendarMeetingCancel = "calendar_meeting_cancel"`. `OutboxPermanentFailureException`. `public static void OutboxProcessor.ApplyFailure(OutboxMessage message, Exception ex, DateTimeOffset now)`.

- [ ] **Step 1: Write the failing tests**

`tests/ONEVO.Tests.Unit/Features/SharedPlatform/OutboxProcessorFailureTests.cs`:

```csharp
using ONEVO.Application.Common.Exceptions;
using ONEVO.Domain.Features.SharedPlatform.Outbox.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform;

public sealed class OutboxProcessorFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PermanentFailure_IsParkedFailedImmediately()
    {
        var message = new OutboxMessage { Status = OutboxMessageStatus.Pending, AttemptCount = 0 };

        OutboxProcessor.ApplyFailure(message, new OutboxPermanentFailureException("connection gone"), Now);

        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(Now, message.ProcessedAt);
    }

    [Fact]
    public void TransientFailure_IsRetriedWithBackoff()
    {
        var message = new OutboxMessage { Status = OutboxMessageStatus.Pending, AttemptCount = 0 };

        OutboxProcessor.ApplyFailure(message, new HttpRequestException("503"), Now);

        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.True(message.NextAttemptAt > Now);
    }
}
```

`tests/ONEVO.Tests.Unit/Features/Calendar/CalendarMeetingCancelOutboxHandlerTests.cs`:

```csharp
using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarMeetingCancelOutboxHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active };
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokens = new();
    private readonly Mock<ITeamsMeetingClient> _teams = new();
    private readonly Mock<IZoomMeetingClient> _zoom = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();

    private CalendarMeetingCancelOutboxHandler BuildSut()
    {
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("token");
        return new CalendarMeetingCancelOutboxHandler(_connections.Object, _tokens.Object, _teams.Object, _zoom.Object, _notifications.Object);
    }

    private string Payload() => JsonSerializer.Serialize(new CalendarMeetingCancelPayload(
        TenantId, CalendarEventMeetingProviders.MicrosoftTeams, "graph-1", _connection.Id, OrganizerUserId, Guid.NewGuid(), "Review"));

    [Fact]
    public async Task Cancels_TheRemoteMeeting()
    {
        await BuildSut().HandleAsync(Payload(), CancellationToken.None);

        _teams.Verify(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectionGone_TellsTheOrganizer_AndParksTheMessage()
    {
        var sut = BuildSut();
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ExternalCalendarConnection?)null);

        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => sut.HandleAsync(Payload(), CancellationToken.None));

        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, OrganizerUserId, CalendarMeetingNoticeCodes.CancelFailed, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderRefusesPermanently_ParksTheMessage()
    {
        var sut = BuildSut();
        _teams.Setup(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Permanent, "403"));

        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => sut.HandleAsync(Payload(), CancellationToken.None));
    }

    [Fact]
    public async Task TransientFailure_Propagates_ForTheOutboxBackoff()
    {
        var sut = BuildSut();
        _teams.Setup(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "503"));

        await Assert.ThrowsAsync<MeetingProviderException>(() => sut.HandleAsync(Payload(), CancellationToken.None));
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_ThenSucceeds()
    {
        var sut = BuildSut();
        _tokens.Setup(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("token2");
        _teams.Setup(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"));

        await sut.HandleAsync(Payload(), CancellationToken.None);

        _teams.Verify(t => t.CancelMeetingAsync("token2", "graph-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

In `DeleteCalendarEventCommandHandlerTests`:
- delete `Handle_EventHasActiveMeeting_CancelsItRemotelyBeforeDeleting`, `Handle_EventHasActiveZoomMeeting_CancelsItViaZoomClientBeforeDeleting` and `Handle_NoActiveMeeting_NeverCallsTeamsClient`;
- remove the `_connections`, `_tokenProvider`, `_teamsClient` and `_zoomClient` fields, and add `private readonly Mock<IOutboxWriter> _outbox = new();`;
- the `BuildSut` return becomes `new DeleteCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _meetings.Object, _outbox.Object, _unitOfWork.Object)`.

Then append:

```csharp
    [Fact]
    public async Task Handle_ActiveMeeting_QueuesASelfContainedCancel_InsideTheTransaction_AndNeverCallsTheProvider()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event", StartDate = DateTimeOffset.UtcNow.AddDays(1), EndDate = DateTimeOffset.UtcNow.AddDays(1).AddHours(1) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Provider = CalendarEventMeetingProviders.Zoom,
            ExternalMeetingId = "987654321", ExternalCalendarConnectionId = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, LinkGeneration = 3
        };
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        var calls = new List<string>();
        _outbox.Setup(o => o.EnqueueKeyedAsync(OutboxMessageTypes.CalendarMeetingCancel, It.IsAny<CalendarMeetingCancelPayload>(),
                $"meeting-cancel:{meeting.Id}:3", TenantId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("queue")).ReturnsAsync(true);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.Equal(new[] { "queue", "save" }, calls);
        _outbox.Verify(o => o.EnqueueKeyedAsync(OutboxMessageTypes.CalendarMeetingCancel,
            It.Is<CalendarMeetingCancelPayload>(p => p.ExternalMeetingId == "987654321" && p.Provider == CalendarEventMeetingProviders.Zoom
                && p.OrganizerUserId == UserId && p.EventTitle == "Event" && p.ExternalCalendarConnectionId == meeting.ExternalCalendarConnectionId),
            It.IsAny<string>(), TenantId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, meeting.Status);
    }

    [Fact]
    public async Task Handle_LocalSaveFails_TheCancelMessageIsNeverCommitted()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, Provider = CalendarEventMeetingProviders.MicrosoftTeams });
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var committed = false;
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>> op, CancellationToken ct) =>
            {
                var result = await op(ct); // throws before this line on a failed save
                committed = true;
                return result;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None));

        Assert.False(committed); // the keyed insert ran inside the transaction, so a rollback removes it
    }
```

(The second test pins the shape that makes X-2 hold: the keyed cancel insert happens **inside** `ExecuteInTransactionAsync`. 0A Task 1's `RolledBackTransaction_LeavesNoKeyedRow` integration test proves a rollback removes the row.) Add `using ONEVO.Application.Common.ServiceInterfaces;` and `using ONEVO.Application.Features.Calendar.OutboxHandlers;` if missing.

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo 2>&1 | grep -E "error CS" | sort -u | head -3` → Expected: missing types.

- [ ] **Step 2: Implement**

`src/ONEVO.Application/Common/Exceptions/OutboxPermanentFailureException.cs`:

```csharp
namespace ONEVO.Application.Common.Exceptions;

/// <summary>Thrown by an outbox handler when retrying can never succeed: the processor parks the
/// message as Failed immediately instead of backing off (the handler has already told whoever needs to act).</summary>
public sealed class OutboxPermanentFailureException(string message, Exception? innerException = null) : Exception(message, innerException);
```

In `OutboxProcessor`, replace the body of `catch (Exception ex)` (from `message.AttemptCount++;` through the end of its `else` block) with `ApplyFailure(message, ex, clock.UtcNow); LogFailure(message, ex);`, and add:

```csharp
    /// <summary>Next state of a message whose handler threw: parked Failed at once on a permanent
    /// failure or after MaxAttempts, otherwise retried with exponential backoff.</summary>
    public static void ApplyFailure(OutboxMessage message, Exception ex, DateTimeOffset now)
    {
        message.AttemptCount++;
        message.LastError = Truncate(ex.Message, 2000);
        if (ex is OutboxPermanentFailureException || message.AttemptCount >= MaxAttempts)
        {
            message.Status = OutboxMessageStatus.Failed;
            message.ProcessedAt = now;
            return;
        }

        var delaySeconds = BaseRetryDelay.TotalSeconds * Math.Pow(2, message.AttemptCount - 1);
        message.NextAttemptAt = now.Add(TimeSpan.FromSeconds(Math.Min(delaySeconds, MaxRetryDelay.TotalSeconds)));
    }

    private void LogFailure(OutboxMessage message, Exception ex)
    {
        if (message.Status == OutboxMessageStatus.Failed)
            _logger.LogError(ex, "Outbox message {MessageId} ({Type}) failed permanently after {Attempts} attempts.",
                message.Id, message.Type, message.AttemptCount);
        else
            _logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) attempt {Attempt} failed; next attempt at {NextAttempt}.",
                message.Id, message.Type, message.AttemptCount, message.NextAttemptAt);
    }
```

Add `using ONEVO.Application.Common.Exceptions;`.

In `OutboxMessageTypes` add `public const string CalendarMeetingCancel = "calendar_meeting_cancel";`.

`src/ONEVO.Application/Features/Calendar/OutboxHandlers/CalendarMeetingCancelOutboxHandler.cs`:

```csharp
using System.Text.Json;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

/// <summary>Everything the cancel needs after the event row is gone (spec X-2).</summary>
public sealed record CalendarMeetingCancelPayload(
    Guid TenantId, string Provider, string ExternalMeetingId, Guid ExternalCalendarConnectionId,
    Guid OrganizerUserId, Guid OrganizerEmployeeId, string EventTitle);

/// <summary>Cancels a deleted event's Teams/Zoom meeting after the delete committed. Transient failures
/// retry through the outbox backoff; a gone connection or a permanent refusal parks the message as
/// Failed and tells the organizer to delete it by hand. Idempotent: a 404 is success.</summary>
public sealed class CalendarMeetingCancelOutboxHandler(
    IExternalCalendarConnectionRepository connections,
    ICalendarConnectionTokenProvider tokenProvider,
    ITeamsMeetingClient teamsClient,
    IZoomMeetingClient zoomClient,
    ICalendarNotificationSender notifications) : IOutboxMessageHandler
{
    public string Type => OutboxMessageTypes.CalendarMeetingCancel;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarMeetingCancelPayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_meeting_cancel payload is empty.");
        var isZoom = payload.Provider == CalendarEventMeetingProviders.Zoom;
        var oauthProvider = isZoom ? "zoom" : "microsoft";

        var connection = await connections.GetByIdForTenantAsync(payload.TenantId, payload.ExternalCalendarConnectionId, ct);
        if (connection is null || connection.Status != ExternalCalendarConnectionStatuses.Active)
        {
            await ParkAsync(payload, "The organizer's meeting connection no longer exists.", ct);
            return;
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
        {
            await ParkAsync(payload, "The organizer's meeting connection needs to be reconnected.", ct);
            return;
        }

        try
        {
            await CancelAsync(isZoom, accessToken, payload.ExternalMeetingId, ct);
        }
        catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.Unauthorized)
        {
            var refreshed = await tokenProvider.ForceRefreshAccessTokenAsync(connection, oauthProvider, ct);
            if (refreshed is null)
            {
                await ParkAsync(payload, "The provider rejected the organizer's credentials.", ct);
                return;
            }
            try
            {
                await CancelAsync(isZoom, refreshed, payload.ExternalMeetingId, ct);
            }
            catch (MeetingProviderException again) when (again.Kind is MeetingProviderFailureKind.Unauthorized or MeetingProviderFailureKind.Permanent)
            {
                await ParkAsync(payload, again.Message, ct);
            }
        }
        catch (MeetingProviderException ex) when (ex.Kind == MeetingProviderFailureKind.Permanent)
        {
            await ParkAsync(payload, ex.Message, ct);
        }
        // RateLimited / Ambiguous / ZoomMeetingInProgressException propagate: the outbox retries later.
    }

    private Task CancelAsync(bool isZoom, string accessToken, string externalMeetingId, CancellationToken ct)
        => isZoom
            ? zoomClient.CancelMeetingAsync(accessToken, externalMeetingId, ct)
            : teamsClient.CancelMeetingAsync(accessToken, externalMeetingId, ct);

    private async Task ParkAsync(CalendarMeetingCancelPayload payload, string reason, CancellationToken ct)
    {
        // Staged on the processor's context; it is saved with the message's Failed state.
        await notifications.NotifyOrganizerAboutMeetingAsync(
            payload.TenantId, payload.OrganizerUserId, CalendarMeetingNoticeCodes.CancelFailed, payload.EventTitle,
            CalendarMeetingProviderNames.Display(payload.Provider), ct);
        throw new OutboxPermanentFailureException(reason);
    }
}
```

Register it after the invite email handler in `src/ONEVO.Application/DependencyInjection.cs:114`: `services.AddScoped<IOutboxMessageHandler, ONEVO.Application.Features.Calendar.OutboxHandlers.CalendarMeetingCancelOutboxHandler>();`.

Replace `DeleteCalendarEventCommandHandler`'s constructor and the meeting block (the 0A Task 6 version) with:

```csharp
public sealed class DeleteCalendarEventCommandHandler(
    ICurrentUser currentUser,
    ICalendarEventRepository events,
    ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository employees,
    ICalendarNotificationSender notifications,
    ICalendarEventMeetingRepository meetings,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCalendarEventCommand, Result>
```

and, inside `ExecuteInTransactionAsync`, in place of the old remote-cancel block:

```csharp
            // X-2: delete locally first. The remote cancel is a self-contained outbox message inserted in
            // THIS transaction, so it exists only if the delete commits.
            var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, existing.Id, innerCt);
            if (meeting is not null)
            {
                var providerName = CalendarMeetingProviderNames.Display(meeting.Provider);
                if (meeting.Status == CalendarEventMeetingStatuses.Active)
                {
                    var organizerEmployee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, innerCt);
                    await outbox.EnqueueKeyedAsync(
                        OutboxMessageTypes.CalendarMeetingCancel,
                        new CalendarMeetingCancelPayload(
                            tenantId, meeting.Provider, meeting.ExternalMeetingId, meeting.ExternalCalendarConnectionId,
                            existing.CreatedById, organizerEmployee?.Id ?? Guid.Empty, existing.Title),
                        $"meeting-cancel:{meeting.Id}:{meeting.LinkGeneration}", tenantId, innerCt);
                    meeting.Status = CalendarEventMeetingStatuses.Cancelled;
                    meetings.Update(meeting);
                }
                else if (meeting.Status is CalendarEventMeetingStatuses.Provisioning
                    or CalendarEventMeetingStatuses.ProvisioningUnknown or CalendarEventMeetingStatuses.Unresolved)
                {
                    // We don't know the remote meeting's ID: the organizer has to check by hand.
                    await notifications.NotifyOrganizerAboutMeetingAsync(
                        tenantId, existing.CreatedById, CalendarMeetingNoticeCodes.Unresolved, existing.Title, providerName, innerCt);
                }
            }
```

Add `using ONEVO.Application.Common.ServiceInterfaces;` and `using ONEVO.Application.Features.Calendar.OutboxHandlers;`.

0A's integration harness constructs `DeleteCalendarEventCommandHandler` with the old arguments (`tests/ONEVO.Tests.Integration/Calendar/CalendarNotificationOutboxIntegrationTests.cs`, `Delete_EmailsTheGuestToo`). Change it to `new DeleteCalendarEventCommandHandler(h.OrganizerUser, s.Events, s.Employees, s.Sender, new EfCalendarEventMeetingRepository(s.Db, new SystemDateTimeProvider()), new OutboxWriter(s.Db, CalendarNotificationDb.Encryption, new SystemDateTimeProvider()), s.UnitOfWork)`.

- [ ] **Step 3: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -nologo --filter "FullyQualifiedName~DeleteCalendarEventCommandHandlerTests|FullyQualifiedName~CalendarMeetingCancelOutboxHandlerTests|FullyQualifiedName~OutboxProcessorFailureTests"` → all pass. `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo` → succeeds.

```bash
git add src/ONEVO.Application src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxProcessor.cs tests
git commit -m "fix(calendar): delete locally first; cancel the remote meeting from the outbox after commit, parking permanent failures

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Integration tests — concurrency, generations and email idempotency on real Postgres

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Calendar/MeetingProvisioningIntegrationTests.cs`

**Interfaces:**
- Consumes: 0A's `CalendarNotificationDb` harness, and every handler from Tasks 3–8.

- [ ] **Step 1: Write the tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.Calendar.CalendarNotificationDb;

namespace ONEVO.Tests.Integration.Calendar;

public sealed class MeetingProvisioningIntegrationTests
{
    /// <summary>Teams double: counts creates, can block until released, and keeps one meeting per externalId (like createOrGet).</summary>
    private sealed class FakeTeams : ITeamsMeetingClient, ICalendarConnectionTokenProvider
    {
        private readonly object _gate = new();
        public int CreateCalls;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block;
        public Dictionary<string, TeamsMeetingDto> ByExternalId { get; } = [];
        public List<string> Cancelled { get; } = [];

        public async Task<TeamsMeetingDto> CreateOrGetMeetingAsync(string a, string externalId, string s, DateTimeOffset b, DateTimeOffset e, CancellationToken ct)
        {
            Interlocked.Increment(ref CreateCalls);
            if (Block) await Release.Task;
            lock (_gate)
            {
                if (!ByExternalId.TryGetValue(externalId, out var dto))
                    ByExternalId[externalId] = dto = new TeamsMeetingDto($"graph-{ByExternalId.Count + 1}", $"https://teams/j/{ByExternalId.Count + 1}", null, null);
                return dto;
            }
        }

        public Task CancelMeetingAsync(string a, string id, CancellationToken ct) { lock (_gate) Cancelled.Add(id); return Task.CompletedTask; }
        Task<TeamsAttendanceResult> ITeamsMeetingClient.GetAttendanceAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => Task.FromResult<string?>("token");
        public Task<string?> ForceRefreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => Task.FromResult<string?>("token");
    }

    private sealed class UnusedZoom : IZoomMeetingClient
    {
        public Task<ZoomMeetingDto> CreateMeetingAsync(string a, string s, DateTimeOffset b, DateTimeOffset e, string t, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(string a, string t, DateTimeOffset s, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task CancelMeetingAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
    }

    private static async Task<ExternalCalendarConnection> AddTeamsConnectionAsync(CalendarNotificationDb h)
    {
        await using var db = h.Context();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.Organizer.UserId, Provider = CalendarExternalSources.OutlookCalendar,
            ExternalAccountEmail = "ada@example.com", RefreshTokenEncrypted = [1], Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = """["OnlineMeetings.ReadWrite"]"""
        };
        db.ExternalCalendarConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    private static (CreateEventMeetingCommandHandler Create, RemoveEventMeetingCommandHandler Remove) Handlers(
        CalendarNotificationDb h, CalendarHandlers s, FakeTeams teams)
    {
        var clock = new SystemDateTimeProvider();
        var meetings = new EfCalendarEventMeetingRepository(s.Db, clock);
        var connections = new EfExternalCalendarConnectionRepository(s.Db);
        var provisioner = new MeetingProvisioner(teams, teams, new UnusedZoom(), NullLogger<MeetingProvisioner>.Instance);
        var links = new EventMeetingLinkService(s.Events, meetings, connections, provisioner, s.Sender, s.UnitOfWork, clock);
        return (
            new CreateEventMeetingCommandHandler(h.OrganizerUser, s.Events, connections, meetings, links, s.Employees, clock, s.UnitOfWork),
            new RemoveEventMeetingCommandHandler(h.OrganizerUser, s.Events, meetings, connections, teams, teams, new UnusedZoom(), links, s.Employees, s.UnitOfWork));
    }

    [Fact]
    public async Task TwoConcurrentFirstCreates_OneProviderCall_TheOtherGetsInProgress()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        await AddTeamsConnectionAsync(h);
        var evt = await h.AddEventAsync([], []);
        var teams = new FakeTeams { Block = true };

        async Task<ONEVO.Application.Common.Models.Result<CreateEventMeetingResult>> CreateAsync()
        {
            await using var s = h.Scope();
            return await Handlers(h, s, teams).Create.Handle(new CreateEventMeetingCommand(evt.Id, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);
        }

        var first = CreateAsync();
        var second = CreateAsync();
        await Task.Delay(500); // both have tried to claim
        teams.Release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, teams.CreateCalls);
        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.Error == "meeting_link_in_progress");
    }

    [Fact]
    public async Task CreateRemoveCreateRemoveCreate_EveryLinkAndLinkRemovedEmailOncePerGeneration_RetriesSendNothing()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        await AddTeamsConnectionAsync(h);
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], ["vendor@example.com"]);
        var teams = new FakeTeams();

        async Task CreateAsync()
        {
            await using var s = h.Scope();
            var result = await Handlers(h, s, teams).Create.Handle(new CreateEventMeetingCommand(evt.Id, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        async Task RemoveAsync()
        {
            await using var s = h.Scope();
            Assert.True((await Handlers(h, s, teams).Remove.Handle(new RemoveEventMeetingCommand(evt.Id), CancellationToken.None)).IsSuccess);
        }

        await CreateAsync();
        await CreateAsync(); // retried request: the existing link, nothing new sent
        await RemoveAsync();
        await CreateAsync();
        await RemoveAsync();
        await CreateAsync();

        var rows = await h.OutboxAsync();
        var linkEmails = rows.Where(r => r.DeduplicationKey?.StartsWith("link:") == true).ToList();
        var removedEmails = Emails(rows, CalendarEmailKinds.LinkRemoved).ToList();
        Assert.Equal(6, linkEmails.Count);    // 3 generations × (participant + guest)
        Assert.Equal(4, removedEmails.Count); // 2 removals × (participant + guest)
        Assert.Equal(3, linkEmails.Select(r => r.DeduplicationKey!.Split(':')[2]).Distinct().Count());
        Assert.Equal(3, teams.ByExternalId.Count); // a new remote meeting per generation
        Assert.Equal(2, teams.Cancelled.Count);
    }

    [Fact]
    public async Task DeleteWithAnActiveMeeting_QueuesOneCancelMessage_AfterCommit()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        await AddTeamsConnectionAsync(h);
        var evt = await h.AddEventAsync([], []);
        var teams = new FakeTeams();
        await using (var s = h.Scope())
            Assert.True((await Handlers(h, s, teams).Create.Handle(new CreateEventMeetingCommand(evt.Id, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None)).IsSuccess);

        await using (var s = h.Scope())
        {
            var clock = new SystemDateTimeProvider();
            var result = await new ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent.DeleteCalendarEventCommandHandler(
                    h.OrganizerUser, s.Events, s.Employees, s.Sender, new EfCalendarEventMeetingRepository(s.Db, clock),
                    new ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter(s.Db, Encryption, clock), s.UnitOfWork)
                .Handle(new ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent.DeleteCalendarEventCommand(evt.Id), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var cancel = Assert.Single((await h.OutboxAsync()).Where(r => r.Type == OutboxMessageTypes.CalendarMeetingCancel));
        Assert.Equal("graph-1", cancel.Payload.RootElement.GetProperty("ExternalMeetingId").GetString());
        Assert.Empty(teams.Cancelled); // not called inline
    }
}
```

`ExternalCalendarConnections` and `OutboxMessageTypes` need `using ONEVO.Application.Common.ServiceInterfaces;`. Check the `ExternalCalendarConnection` required properties in its entity file before running, and add any non-nullable ones it needs.

- [ ] **Step 2: Run them**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~MeetingProvisioningIntegrationTests|FullyQualifiedName~MeetingClaimIntegrationTests|FullyQualifiedName~CalendarNotificationOutboxIntegrationTests"`
Expected: all pass. A failure means a real defect in Tasks 3–8 (claim, keys or ordering). Fix the code, not the expectation.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Calendar/MeetingProvisioningIntegrationTests.cs
git commit -m "test(calendar-meetings): concurrency, link generations and after-commit cancel on real Postgres

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Frontend — say what happened, and offer Retry / Remove / Confirm

Work in `C:\Users\User\onexso-worktrees\fe-meeting-task`.

**Files:**
- Modify: `src/app/modules/calendar/data-access/calendar-event-api.service.ts`, `src/app/modules/calendar/models/calendar-event.model.ts`
- Modify: `src/app/modules/calendar/state/calendar-event.store.ts`
- Modify: `src/app/modules/calendar/feature/calendar-page/calendar-page.component.ts`
- Create: `src/app/modules/calendar/ui/calendar-event-meeting-status/calendar-event-meeting-status.component.ts` + `.spec.ts`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.html` / `.ts`
- Create: `src/app/modules/calendar/utils/meeting-error-message.ts` + `.spec.ts`

**Interfaces:**
- Consumes: backend endpoints and error codes from Task 6.

- [ ] **Step 1: Write the failing tests**

`src/app/modules/calendar/utils/meeting-error-message.spec.ts`:

```ts
import { HttpErrorResponse } from '@angular/common/http';
import { meetingErrorMessage } from './meeting-error-message';

const problem = (detail: string, status = 409) => new HttpErrorResponse({ status, error: { detail } });

describe('meetingErrorMessage', () => {
  it('explains a failed link and that invitations still went out', () => {
    expect(meetingErrorMessage(problem('meeting_link_failed', 502), 'microsoft_teams'))
      .toBe("The event is saved, but the Microsoft Teams link couldn't be created. Invitations were sent without a link; you can add a meeting from the event.");
  });

  it('explains an unknown link status', () => {
    expect(meetingErrorMessage(problem('meeting_link_status_unknown'), 'zoom'))
      .toContain("couldn't confirm whether the Zoom meeting was created");
  });

  it('explains a missing connection', () => {
    expect(meetingErrorMessage(problem('meeting_provider_not_connected'), 'zoom')).toContain('Connect Zoom');
  });

  it('falls back to a generic message', () => {
    expect(meetingErrorMessage(new Error('boom'), 'zoom')).toBe('Failed to add the meeting.');
  });
});
```

`src/app/modules/calendar/ui/calendar-event-meeting-status/calendar-event-meeting-status.component.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { CalendarEventApiService } from '../../data-access/calendar-event-api.service';
import { CalendarEventMeetingStatusComponent } from './calendar-event-meeting-status.component';

describe('CalendarEventMeetingStatusComponent', () => {
  function setup(status: string, provider = 'zoom') {
    const api = {
      getMeeting: vi.fn().mockReturnValue(of({ status, provider, joinUrl: null, failureReason: null })),
      retryMeeting: vi.fn().mockReturnValue(of({ joinUrl: 'https://zoom.us/j/1' })),
      removeMeeting: vi.fn().mockReturnValue(of(undefined)),
      confirmMeetingCleanup: vi.fn().mockReturnValue(of(undefined))
    };
    TestBed.configureTestingModule({
      imports: [CalendarEventMeetingStatusComponent],
      providers: [{ provide: CalendarEventApiService, useValue: api }]
    });
    const fixture = TestBed.createComponent(CalendarEventMeetingStatusComponent);
    fixture.componentRef.setInput('eventId', 'event-1');
    fixture.detectChanges();
    return { fixture, api };
  }

  it('renders nothing for an active meeting', () => {
    const { fixture } = setup('active');
    expect(fixture.nativeElement.querySelector('[data-testid="calendar-meeting-status"]')).toBeNull();
  });

  it('offers Retry and Remove when the status is unknown', () => {
    const { fixture, api } = setup('provisioning_unknown');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Meeting link status unknown');
    (fixture.nativeElement.querySelector('[data-testid="calendar-meeting-retry"]') as HTMLButtonElement).click();
    expect(api.retryMeeting).toHaveBeenCalledWith('event-1');
    expect(fixture.nativeElement.querySelector('[data-testid="calendar-meeting-remove"]')).not.toBeNull();
  });

  it('asks the organizer to clean up and confirm when unresolved', () => {
    const { fixture, api } = setup('unresolved');
    expect(fixture.nativeElement.textContent).toContain('may still exist');
    (fixture.nativeElement.querySelector('[data-testid="calendar-meeting-confirm-cleanup"]') as HTMLButtonElement).click();
    expect(api.confirmMeetingCleanup).toHaveBeenCalledWith('event-1');
  });

  it('says the link could not be created when failed', () => {
    const { fixture } = setup('failed', 'microsoft_teams');
    expect(fixture.nativeElement.textContent).toContain("couldn't be created");
  });
});
```

Run: `npx ng test --watch=false --include src/app/modules/calendar/utils/meeting-error-message.spec.ts --include src/app/modules/calendar/ui/calendar-event-meeting-status/calendar-event-meeting-status.component.spec.ts`
Expected: FAIL (modules missing).

- [ ] **Step 2: Implement the API, model and message helper**

In `calendar-event.model.ts` add:

```ts
export type EventMeetingStatus = 'none' | 'active' | 'cancelled' | 'failed' | 'provisioning' | 'provisioning_unknown' | 'unresolved';

export interface EventMeetingStatusResponse {
  status: EventMeetingStatus;
  provider: MeetingProvider | null;
  joinUrl: string | null;
  failureReason: string | null;
}
```

In `CalendarEventApiService`, after `removeMeeting`, add:

```ts
  getMeeting(eventId: string): Observable<EventMeetingStatusResponse> {
    return this.http.get<EventMeetingStatusResponse>(`${this.baseUrl}/${eventId}/meeting`);
  }

  retryMeeting(eventId: string): Observable<CreateEventMeetingResponse> {
    return this.http.post<CreateEventMeetingResponse>(`${this.baseUrl}/${eventId}/meeting/retry`, {});
  }

  confirmMeetingCleanup(eventId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${eventId}/meeting/confirm-cleanup`, {});
  }
```

(import `EventMeetingStatusResponse` beside the existing model imports).

`src/app/modules/calendar/utils/meeting-error-message.ts`:

```ts
import { HttpErrorResponse } from '@angular/common/http';

const providerName = (provider: string | null | undefined): string => (provider === 'zoom' ? 'Zoom' : 'Microsoft Teams');

/** Turns the backend's meeting problem codes into what happened and what the organizer can do. */
export function meetingErrorMessage(err: unknown, provider: string | null | undefined): string {
  const detail = err instanceof HttpErrorResponse ? (err.error as { detail?: string } | null)?.detail : undefined;
  const name = providerName(provider);
  switch (detail) {
    case 'meeting_link_failed':
      return `The event is saved, but the ${name} link couldn't be created. Invitations were sent without a link; you can add a meeting from the event.`;
    case 'meeting_link_status_unknown':
      return `The event is saved, but we couldn't confirm whether the ${name} meeting was created. Open the event to retry or remove it.`;
    case 'meeting_provider_not_connected':
      return `Connect ${name} in calendar settings to add a ${name} meeting. Invitations were sent without a link.`;
    case 'meeting_link_in_progress':
      return `A ${name} link is already being created for this event.`;
    case 'meeting_link_unresolved':
      return `A ${name} meeting for this event may still exist. Delete it in ${name}, then confirm in the event.`;
    default:
      return 'Failed to add the meeting.';
  }
}
```

- [ ] **Step 3: Implement the status component**

`src/app/modules/calendar/ui/calendar-event-meeting-status/calendar-event-meeting-status.component.ts`:

```ts
import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CalendarEventApiService } from '../../data-access/calendar-event-api.service';
import { EventMeetingStatusResponse } from '../../models/calendar-event.model';
import { meetingErrorMessage } from '../../utils/meeting-error-message';

/** Organizer-only panel for a meeting link that isn't simply active: unknown (Retry/Remove),
 * unresolved (clean up, then confirm) or failed. Renders nothing otherwise, or for non-organizers. */
@Component({
  selector: 'app-calendar-event-meeting-status',
  standalone: true,
  template: `
    @if (state(); as s) {
      @if (s.status === 'provisioning_unknown' || s.status === 'unresolved' || s.status === 'failed') {
        <div data-testid="calendar-meeting-status" class="rounded-md border border-[var(--color-border)] p-2 text-xs" role="status">
          @switch (s.status) {
            @case ('provisioning_unknown') {
              <p class="font-medium">Meeting link status unknown</p>
              <p class="text-[var(--color-text-secondary)]">We couldn't confirm whether the {{ name() }} meeting was created. Invitations went out without a link.</p>
              <div class="mt-2 flex gap-2">
                <button type="button" data-testid="calendar-meeting-retry" class="text-[var(--color-accent)]" [disabled]="busy()" (click)="retry()">Retry</button>
                <button type="button" data-testid="calendar-meeting-remove" class="text-red-600" [disabled]="busy()" (click)="remove()">Remove</button>
              </div>
            }
            @case ('unresolved') {
              <p class="font-medium">A {{ name() }} meeting may still exist</p>
              <p class="text-[var(--color-text-secondary)]">Please check and delete it in {{ name() }}, then confirm here.</p>
              <button type="button" data-testid="calendar-meeting-confirm-cleanup" class="mt-2 text-[var(--color-accent)]" [disabled]="busy()" (click)="confirmCleanup()">
                I've deleted it
              </button>
            }
            @case ('failed') {
              <p>The {{ name() }} link couldn't be created. You can add a meeting again.</p>
            }
          }
          @if (error()) {
            <p class="mt-1 text-red-600" role="alert">{{ error() }}</p>
          }
        </div>
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CalendarEventMeetingStatusComponent implements OnInit {
  private readonly api = inject(CalendarEventApiService);

  eventId = input.required<string>();
  readonly changed = output<void>();

  protected readonly state = signal<EventMeetingStatusResponse | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  protected name(): string {
    return this.state()?.provider === 'zoom' ? 'Zoom' : 'Microsoft Teams';
  }

  protected retry(): Promise<void> {
    return this.act(() => firstValueFrom(this.api.retryMeeting(this.eventId())));
  }

  protected remove(): Promise<void> {
    return this.act(() => firstValueFrom(this.api.removeMeeting(this.eventId())));
  }

  protected confirmCleanup(): Promise<void> {
    return this.act(() => firstValueFrom(this.api.confirmMeetingCleanup(this.eventId())));
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.changed.emit();
    } catch (err) {
      this.error.set(meetingErrorMessage(err, this.state()?.provider));
    } finally {
      this.busy.set(false);
      await this.load();
    }
  }

  private async load(): Promise<void> {
    try {
      this.state.set(await firstValueFrom(this.api.getMeeting(this.eventId())));
    } catch {
      this.state.set(null); // not the organizer (403) or no access: show nothing
    }
  }
}
```

- [ ] **Step 4: Wire it in**

In `calendar-event-form-modal.component.ts`, add `CalendarEventMeetingStatusComponent` to `imports` and `@Output() meetingStatusChanged = new EventEmitter<void>();`. In the `.html`, insert this as the first child of `<div class="flex min-w-0 flex-1 flex-col gap-1">` in the meeting row (line ~365):

```html
        @if (isEdit && event) {
          <app-calendar-event-meeting-status [eventId]="event.id" (changed)="meetingStatusChanged.emit()" />
        }
```

In `calendar-page.component.html`, on the `<app-calendar-event-form-modal …>` element, add `(meetingStatusChanged)="onMeetingStatusChanged()"`. In `calendar-page.component.ts` add:

```ts
  async onMeetingStatusChanged(): Promise<void> {
    const editing = this.editingEvent();
    await this.reloadEvents();
    if (editing) this.editingEvent.set(this.store.events().find((e) => e.id === editing.id) ?? null);
  }
```

In `calendar-event.store.ts`, `createMeeting`'s catch becomes:

```ts
      } catch (err) {
        patchState(store, { meetingActionSaving: false, actionError: meetingErrorMessage(err, provider) });
        return false;
      }
```

(import `meetingErrorMessage` from `../utils/meeting-error-message`). In `calendar-page.component.ts`'s create branch, after `await this.store.createMeeting(created.id, submission.meetingProvider);`, nothing else changes: the event already exists and is reloaded, and the store's `actionError` effect shows the specific toast. Add a comment there: `// A failed link keeps the event; the store's actionError explains what happened (P1-5).`

- [ ] **Step 5: Run the tests and type-check**

Run: `npx ng test --watch=false --include src/app/modules/calendar/utils/meeting-error-message.spec.ts --include src/app/modules/calendar/ui/calendar-event-meeting-status/calendar-event-meeting-status.component.spec.ts --include src/app/modules/calendar/state/calendar-event.store.spec.ts`
Expected: all pass. If `calendar-event.store.spec.ts` asserted the old `'Failed to add the meeting.'` for an `HttpErrorResponse` with a known code, update that expectation to the new message.
Run: `npx ng build --configuration development` → success.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/calendar
git commit -m "feat(calendar-meetings): explain failed or unknown meeting links and offer retry, remove and confirm

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Verification and push

- [ ] **Step 1:** `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -nologo` → 0 errors. The unit and architecture suites → `Failed: 0`.
- [ ] **Step 2:** `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -nologo --filter "FullyQualifiedName~Calendar|FullyQualifiedName~Outbox"` → `Failed: 0`.
- [ ] **Step 3:** Frontend `npx ng test --watch=false` → only the pre-existing date-dependent `task-form-modal` failure (Phase 1 closure report §4).
- [ ] **Step 4:** `git push origin feature/meeting-task-phase0-1` in both worktrees (no PR). Report the Zoom scope prerequisite to the user: enable `meeting:read:list_meetings` on the Zoom Marketplace app, then existing Zoom users reconnect.
