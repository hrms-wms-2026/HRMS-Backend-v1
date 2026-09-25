# Zoom Meeting Integration (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an event organizer auto-generate a real Zoom meeting when creating a calendar event (alongside the already-shipped Teams option), and sync post-meeting attendance the same way Teams already does.

**Architecture:** Extends the existing, already-generic `calendar_event_meetings`/`calendar_event_meeting_attendances` tables and `ICalendarConnectionTokenProvider` with a second concrete provider. Every place currently hardcoded to Microsoft/Teams (`CreateEventMeetingCommandHandler`, `RemoveEventMeetingCommandHandler`, `CompleteCalendarConnectionCommandHandler`, `ICalendarOAuthTokenExchangeClient.GetAccountAsync`) gets a Zoom branch alongside its existing Microsoft branch, following the exact same explicit-ternary style those files already use for Google vs. Microsoft — this codebase does not introduce a strategy/factory abstraction for two providers, so this plan doesn't either. Attendance sync reuses the Teams polling-job shape exactly (`ZoomAttendanceSyncJob` mirrors `TeamsAttendanceSyncJob`), per the spec's decision to drop the webhook approach.

**Tech Stack:** .NET 10 / EF Core / PostgreSQL (RLS), MediatR, `HttpClient` (Zoom REST API v2, base URL `https://api.zoom.us/v2`), Angular 18 signals/`signalStore`.

**Spec:** `docs/superpowers/specs/2026-09-23-teams-zoom-meeting-integration-design.md` (updated 2026-09-24 with live-verified Zoom OAuth scopes and the polling-over-webhook decision).

## Global Constraints

- Zoom OAuth scopes (verified live against a real Zoom Marketplace app 2026-09-24): `meeting:write:meeting` (create), `meeting:delete:meeting` (cancel), `meeting:read:meeting` (read), `meeting:read:list_past_participants` (attendance).
- Zoom REST API base URL: `https://api.zoom.us/v2`.
- `CalendarEventMeetingProviders.Zoom = "zoom"` already exists in `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEventMeeting.cs` (currently commented "unused until Phase 2") — this plan makes it live, do not rename it.
- No new webhook controller, no Event Subscriptions — attendance sync is pull-based (`meeting:read:list_past_participants`), mirroring `TeamsAttendanceSyncJob`.
- Codebase convention: provider branching is explicit ternary/switch inline in the handler, not a strategy interface — follow the existing style in `CalendarOAuthTokenExchangeClient.GetAccountAsync` and `CompleteCalendarConnectionCommandHandler`.
- TDD throughout: write the failing test, run it, implement, run again, commit — matching this codebase's existing xUnit + Moq + FluentAssertions patterns exactly (see each task's file references for the pattern to copy).

---

### Task 1: `PlatformOAuthProviderCatalog` — verified Zoom scopes + meetings capability

**Files:**
- Modify: `src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Helpers/PlatformOAuthProviderCatalog.cs:69-76`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/PlatformOAuthAppsTests.cs`

**Interfaces:**
- Produces: `PlatformOAuthProviderCatalog.TryGet("zoom", ...)` now returns a definition whose `DefaultScopes` includes the 4 verified scopes and whose `Capabilities` includes `CapabilityMeetings`. Every later task that resolves a Zoom OAuth app via `IPlatformOAuthAppResolver` relies on this.

- [ ] **Step 1: Write the failing test**

Open `tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/PlatformOAuthAppsTests.cs` and add (mirroring the existing `Catalog_Microsoft_RequestsOnlineMeetingsScopeAndAdvertisesMeetingsCapability` test in the same file):

```csharp
[Fact]
public void Catalog_Zoom_RequestsMeetingScopesAndAdvertisesMeetingsCapability()
{
    var found = PlatformOAuthProviderCatalog.TryGet("zoom", out var definition);

    Assert.True(found);
    Assert.Contains("meeting:write:meeting", definition.DefaultScopes);
    Assert.Contains("meeting:delete:meeting", definition.DefaultScopes);
    Assert.Contains("meeting:read:meeting", definition.DefaultScopes);
    Assert.Contains("meeting:read:list_past_participants", definition.DefaultScopes);
    Assert.Contains(PlatformOAuthProviderCatalog.CapabilityMeetings, definition.Capabilities);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter Catalog_Zoom_RequestsMeetingScopesAndAdvertisesMeetingsCapability`
Expected: FAIL — current `zoom` entry's `DefaultScopes` is only `["meeting:read"]` and `Capabilities` is only `[CapabilityUserOAuth]`.

- [ ] **Step 3: Update the catalog entry**

In `PlatformOAuthProviderCatalog.cs`, replace the existing `zoom` entry (lines 69-76):

```csharp
            // Scopes verified live against a real Zoom Marketplace "General App" (User-managed)
            // 2026-09-24 — see docs/superpowers/specs/2026-09-23-teams-zoom-meeting-integration-design.md.
            // meeting:read:list_past_participants is deliberately chosen over the live
            // meeting:read:participant scope: attendance is only ever synced after the event's end
            // time has passed (ZoomAttendanceSyncJob mirrors TeamsAttendanceSyncJob's timing), so
            // the live-participant scope would be requested and never used.
            new PlatformOAuthProviderDefinition(
                Provider: "zoom",
                DisplayName: "Zoom",
                AuthorizationUrl: "https://zoom.us/oauth/authorize",
                TokenUrl: "https://zoom.us/oauth/token",
                DefaultScopes: new[]
                {
                    "meeting:write:meeting", "meeting:delete:meeting",
                    "meeting:read:meeting", "meeting:read:list_past_participants"
                },
                ClientSecretRequired: true,
                Capabilities: new[] { CapabilityUserOAuth, CapabilityMeetings })
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter Catalog_Zoom_RequestsMeetingScopesAndAdvertisesMeetingsCapability`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/SystemConfig/PlatformOAuthApps/Helpers/PlatformOAuthProviderCatalog.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/SystemConfig/PlatformOAuthAppsTests.cs
git commit -m "feat(calendar): update Zoom OAuth catalog entry with verified meeting scopes"
```

---

### Task 2: `CalendarExternalSources.Zoom` + wire Zoom into the connection callback

**Why:** `CompleteCalendarConnectionCommandHandler.CompleteConnectionAsync` currently has a binary branch (`request.Provider.Equals("google") ? GoogleCalendar : OutlookCalendar`) — any non-Google provider, including a future `"zoom"` callback, is silently miscategorized as Outlook today. This task fixes that and makes Zoom connections skip the calendar-sync job (they're meeting-only, not calendar-sync connections).

**Files:**
- Modify: `src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs:14-19`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommandHandler.cs:99-101,145`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs`

**Interfaces:**
- Produces: `CalendarExternalSources.Zoom = "zoom"` — later tasks (3, 5, 6) use this as the `ExternalCalendarConnection.Provider` value for Zoom connections.

- [ ] **Step 1: Write the failing test**

Add to `tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs` (copy the shape of `Handle_HappyPath_CreatesConnectionAndReturnsSuccessRedirect`, changing provider to `"zoom"`):

```csharp
[Fact]
public async Task Handle_ZoomHappyPath_CreatesZoomConnectionWithSyncDisabled()
{
    var sut = BuildSut();
    var state = new CalendarOAuthState("nonce", TenantId, UserId, "zoom", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
    _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
    var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
    _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
    _appResolver.Setup(x => x.GetActiveAppForProviderAsync("zoom", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ResolvedPlatformOAuthApp("zoom", "client", "https://zoom.us/oauth/authorize", "https://zoom.us/oauth/token", ["meeting:write:meeting"]));
    _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("zoom", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("zoom", "client", "secret", null, 1));
    _tokenClient.Setup(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new CalendarProviderTokens("at-1", "rt-1", DateTimeOffset.UtcNow.AddHours(1)));
    _tokenClient.Setup(x => x.GetAccountAsync("zoom", "at-1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new CalendarProviderAccount("me@acme.com", null, null));
    _connections.Setup(x => x.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.Zoom, It.IsAny<CancellationToken>()))
        .ReturnsAsync((ExternalCalendarConnection?)null);
    _encryption.Setup(x => x.EncryptBytes(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));

    var result = await sut.Handle(new CompleteCalendarConnectionCommand("zoom", "code", "state"), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Contains("connected=zoom", result.Value);
    _connections.Verify(x => x.AddAsync(It.Is<ExternalCalendarConnection>(c =>
        c.Provider == CalendarExternalSources.Zoom && c.SyncDirection == CalendarSyncDirections.Disabled), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter Handle_ZoomHappyPath_CreatesZoomConnectionWithSyncDisabled`
Expected: FAIL — `CalendarExternalSources.Zoom` doesn't exist yet (compile error), and the handler's else-branch would categorize `"zoom"` as `OutlookCalendar` with `SyncDirection = TwoWay`.

- [ ] **Step 3: Add the constant and wire the handler**

In `CalendarEvent.cs`, extend `CalendarExternalSources`:

```csharp
public static class CalendarExternalSources
{
    public const string GoogleCalendar = "google_calendar";
    public const string OutlookCalendar = "outlook_calendar";
    public const string CountryHoliday = "country_holiday";
    // Meeting-only connection (no calendar sync) — see CompleteCalendarConnectionCommandHandler,
    // which sets SyncDirection = Disabled for this provider so CalendarSyncJob skips it.
    public const string Zoom = "zoom";
}
```

In `CompleteCalendarConnectionCommandHandler.cs`, replace lines 99-101:

```csharp
        var externalSource = request.Provider switch
        {
            var p when p.Equals("google", StringComparison.OrdinalIgnoreCase) => CalendarExternalSources.GoogleCalendar,
            var p when p.Equals("zoom", StringComparison.OrdinalIgnoreCase) => CalendarExternalSources.Zoom,
            _ => CalendarExternalSources.OutlookCalendar
        };
```

And in the same file, the new-connection branch (around line 145) — change the single `SyncDirection = CalendarSyncDirections.TwoWay,` line to:

```csharp
                    // Zoom connections are meeting-only — there is no calendar to sync, and
                    // CalendarSyncService/CalendarSyncJob already skip any connection whose
                    // SyncDirection is Disabled (see CalendarSyncService.SyncConnectionAsync).
                    SyncDirection = externalSource == CalendarExternalSources.Zoom
                        ? CalendarSyncDirections.Disabled
                        : CalendarSyncDirections.TwoWay,
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CompleteCalendarConnectionCommandHandlerTests`
Expected: PASS (all tests in the file, including the new one and the pre-existing Google ones which must still pass unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/Calendar/Entities/CalendarEvent.cs src/ONEVO.Application/Features/Calendar/Commands/CompleteCalendarConnection/CompleteCalendarConnectionCommandHandler.cs tests/ONEVO.Tests.Unit/Features/Calendar/CompleteCalendarConnectionCommandHandlerTests.cs
git commit -m "fix(calendar): correctly categorize Zoom connections instead of defaulting to Outlook"
```

---

### Task 3: Zoom account lookup in `CalendarOAuthTokenExchangeClient`

**Files:**
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Calendar/CalendarOAuthTokenExchangeClient.cs:61-66`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthTokenExchangeClientTests.cs` (if this file doesn't exist yet, create it following `MicrosoftGraphMeetingClientTests.cs`'s `StubHandler` pattern)

**Interfaces:**
- Consumes: none new.
- Produces: `GetAccountAsync("zoom", accessToken, ct)` returns a `CalendarProviderAccount` with `PrimaryCalendarId: null, PrimaryCalendarName: null` (Zoom has no calendar concept) — used by Task 2's callback handler.

- [ ] **Step 1: Check whether the test file exists**

Run: `ls tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthTokenExchangeClientTests.cs`

If it exists, open it and match its existing `StubHandler`/setup pattern for the new test below. If it does not exist, create it using the exact `StubHandler` class from `tests/ONEVO.Tests.Unit/Features/Calendar/MicrosoftGraphMeetingClientTests.cs:11-15`.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task GetAccountAsync_Zoom_ReturnsEmailWithNoCalendarInfo()
{
    var handler = new StubHandler(request =>
    {
        Assert.Equal("https://api.zoom.us/v2/users/me", request.RequestUri!.ToString());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { email = "organizer@acme.com" })
        };
    });
    var client = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

    var result = await client.GetAccountAsync("zoom", "access-token", CancellationToken.None);

    Assert.Equal("organizer@acme.com", result.AccountEmail);
    Assert.Null(result.PrimaryCalendarId);
    Assert.Null(result.PrimaryCalendarName);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetAccountAsync_Zoom_ReturnsEmailWithNoCalendarInfo`
Expected: FAIL — `GetAccountAsync` currently routes anything non-"google" to `GetMicrosoftAccountAsync`, which would call the wrong URL and throw or mis-parse the stub response.

- [ ] **Step 4: Implement the Zoom branch**

In `CalendarOAuthTokenExchangeClient.cs`, replace the `GetAccountAsync` method (lines 61-66):

```csharp
    public async Task<CalendarProviderAccount> GetAccountAsync(string provider, string accessToken, CancellationToken ct)
    {
        if (provider.Equals("google", StringComparison.OrdinalIgnoreCase))
            return await GetGoogleAccountAsync(accessToken, ct);
        if (provider.Equals("zoom", StringComparison.OrdinalIgnoreCase))
            return await GetZoomAccountAsync(accessToken, ct);
        return await GetMicrosoftAccountAsync(accessToken, ct);
    }

    private async Task<CalendarProviderAccount> GetZoomAccountAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.zoom.us/v2/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var email = doc.RootElement.GetProperty("email").GetString()!;
        // Zoom has no calendar concept (unlike Google/Microsoft) — this connection is
        // meeting-only, so there is no primary calendar id/name to report.
        return new CalendarProviderAccount(email, null, null);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CalendarOAuthTokenExchangeClientTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/ExternalServices/Calendar/CalendarOAuthTokenExchangeClient.cs tests/ONEVO.Tests.Unit/Features/Calendar/CalendarOAuthTokenExchangeClientTests.cs
git commit -m "feat(calendar): resolve Zoom account email during connection setup"
```

---

### Task 4: `IZoomMeetingClient` + `ZoomMeetingClient`

**Files:**
- Create: `src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IZoomMeetingClient.cs`
- Create: `src/ONEVO.Infrastructure/ExternalServices/Calendar/ZoomMeetingClient.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:503` (add HttpClient registration next to the existing `ITeamsMeetingClient` one)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/ZoomMeetingClientTests.cs`

**Interfaces:**
- Produces: `IZoomMeetingClient.CreateMeetingAsync/CancelMeetingAsync/GetAttendanceAsync` — same three-method shape as `ITeamsMeetingClient` (deliberately not sharing the interface or DTOs — see Global Constraints on explicit-branching style), returning `ZoomMeetingDto`/`ZoomAttendanceRecordDto`. Consumed by Task 5 (`CreateEventMeetingCommandHandler`), Task 6 (`RemoveEventMeetingCommandHandler`), Task 7 (`ZoomAttendanceSyncJob`).

- [ ] **Step 1: Write the interface**

```csharp
// src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IZoomMeetingClient.cs
namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record ZoomMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record ZoomAttendanceRecordDto(
    string? ParticipantName, string? ParticipantEmail, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public interface IZoomMeetingClient
{
    Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/Calendar/ZoomMeetingClientTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class ZoomMeetingClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task CreateMeetingAsync_PostsToMeetingsEndpoint_ReturnsJoinInfo()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.zoom.us/v2/users/me/meetings", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    id = 987654321L,
                    join_url = "https://us05web.zoom.us/j/987654321?pwd=abc",
                    start_url = "https://us05web.zoom.us/s/987654321?zak=xyz",
                    password = "123456"
                })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.CreateMeetingAsync(
            "access-token", "Sprint planning", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);

        Assert.Equal("987654321", result.ExternalMeetingId);
        Assert.Equal("https://us05web.zoom.us/j/987654321?pwd=abc", result.JoinUrl);
        Assert.Equal("https://us05web.zoom.us/s/987654321?zak=xyz", result.OrganizerJoinUrl);
        Assert.Equal("123456", result.PasscodeOrPin);
    }

    [Fact]
    public async Task CancelMeetingAsync_SendsDeleteToTheMeetingsExternalId()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("https://api.zoom.us/v2/meetings/987654321", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None);
    }

    [Fact]
    public async Task GetAttendanceAsync_ReturnsFlattenedParticipants()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(
                "https://api.zoom.us/v2/past_meetings/987654321/participants",
                request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    participants = new[]
                    {
                        new
                        {
                            name = "Ada Lovelace", user_email = "ada@acme.com",
                            join_time = "2026-09-23T10:00:00Z", leave_time = "2026-09-23T10:30:00Z"
                        }
                    }
                })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", CancellationToken.None);

        var record = Assert.Single(result);
        Assert.Equal("Ada Lovelace", record.ParticipantName);
        Assert.Equal("ada@acme.com", record.ParticipantEmail);
        Assert.NotNull(record.LeftAt);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ZoomMeetingClientTests`
Expected: FAIL — `ZoomMeetingClient` doesn't exist yet (compile error).

- [ ] **Step 4: Implement `ZoomMeetingClient`**

```csharp
// src/ONEVO.Infrastructure/ExternalServices/Calendar/ZoomMeetingClient.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class ZoomMeetingClient(HttpClient httpClient, ILogger<ZoomMeetingClient> logger) : IZoomMeetingClient
{
    public async Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.zoom.us/v2/users/me/meetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var durationMinutes = Math.Max(1, (int)(end - start).TotalMinutes);
        request.Content = JsonContent.Create(new
        {
            topic = subject,
            type = 2, // scheduled meeting
            start_time = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            duration = durationMinutes,
            timezone = "UTC",
            settings = new { join_before_host = false, waiting_room = true }
        });
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "create meeting", ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var externalMeetingId = root.GetProperty("id").GetInt64().ToString();
        var joinUrl = root.GetProperty("join_url").GetString()!;
        var organizerJoinUrl = root.TryGetProperty("start_url", out var startUrl) ? startUrl.GetString() : null;
        var passcode = root.TryGetProperty("password", out var pwd) ? pwd.GetString() : null;

        return new ZoomMeetingDto(externalMeetingId, joinUrl, organizerJoinUrl, passcode);
    }

    public async Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.zoom.us/v2/meetings/{externalMeetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "cancel meeting", ct);
    }

    public async Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // Zoom's past-meeting participants report, the direct analogue of Microsoft Graph's
        // attendanceReports endpoint — see MicrosoftGraphMeetingClient.GetAttendanceAsync for the
        // Teams equivalent. Phase 2 takes only the first page (up to Zoom's default page size),
        // matching Teams' own simplification of "non-recurring single-session events only".
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.zoom.us/v2/past_meetings/{externalMeetingId}/participants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "list past meeting participants", ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<ZoomAttendanceRecordDto>();
        foreach (var participant in doc.RootElement.GetProperty("participants").EnumerateArray())
        {
            var name = participant.TryGetProperty("name", out var n) ? n.GetString() : null;
            var email = participant.TryGetProperty("user_email", out var e) ? e.GetString() : null;
            var joined = participant.GetProperty("join_time").GetDateTimeOffset();
            var left = participant.TryGetProperty("leave_time", out var l) && l.ValueKind != JsonValueKind.Null
                ? l.GetDateTimeOffset() : (DateTimeOffset?)null;
            result.Add(new ZoomAttendanceRecordDto(name, email, joined, left));
        }
        return result;
    }

    /// <summary>Same reasoning as MicrosoftGraphMeetingClient.EnsureSuccessOrLogAsync — logs
    /// Zoom's actual error body (invalid field, missing scope, licensing issue) before throwing,
    /// since EnsureSuccessStatusCode() alone discards it.</summary>
    private async Task EnsureSuccessOrLogAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Zoom {Operation} returned {StatusCode}: {ErrorBody}",
                operation, (int)response.StatusCode, errorBody);
        }
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ZoomMeetingClientTests`
Expected: PASS

- [ ] **Step 6: Register the HttpClient in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, immediately after line 503 (`services.AddHttpClient<ITeamsMeetingClient, MicrosoftGraphMeetingClient>(...)`), add:

```csharp
        services.AddHttpClient<IZoomMeetingClient, ZoomMeetingClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
```

- [ ] **Step 7: Verify the build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/ServiceInterfaces/IZoomMeetingClient.cs src/ONEVO.Infrastructure/ExternalServices/Calendar/ZoomMeetingClient.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/ZoomMeetingClientTests.cs
git commit -m "feat(calendar): add Zoom REST API meeting client (create/cancel/attendance)"
```

---

### Task 5: Wire `CreateEventMeetingCommand` to support both providers

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommand.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs:112-120`
- Modify: `src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs` (add request model)
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IZoomMeetingClient` (Task 4), `CalendarExternalSources.Zoom` (Task 2).
- Produces: `CreateEventMeetingCommand(Guid EventId, string Provider)` — `Provider` is `CalendarEventMeetingProviders.MicrosoftTeams` or `.Zoom`. The `POST /api/v1/calendar/{id}/meeting` endpoint now requires a JSON body `{ "provider": "microsoft_teams" | "zoom" }`. Task 10 (frontend) sends this.

- [ ] **Step 1: Write the failing tests**

In `CreateEventMeetingCommandHandlerTests.cs`, update `BuildSut()` to also inject a `Mock<IZoomMeetingClient>` and change every `new CreateEventMeetingCommand(EventId)` call to `new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.MicrosoftTeams)` (the 3 existing Teams tests keep passing unchanged in behavior). Then add:

```csharp
private readonly Mock<IZoomMeetingClient> _zoomClient = new();

// inside BuildSut(), update the constructor call to:
// new CreateEventMeetingCommandHandler(
//     _currentUser.Object, _events.Object, _connections.Object, _tokenProvider.Object,
//     _teamsClient.Object, _zoomClient.Object, _meetings.Object, _unitOfWork.Object);

[Fact]
public async Task Handle_ValidZoomConnection_CreatesZoomMeetingAndSetsMeetingLink()
{
    var evt = MakeEvent();
    var connection = new ExternalCalendarConnection
    {
        Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
        ScopesJson = "[\"meeting:write:meeting\",\"meeting:read:meeting\"]"
    };
    _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(evt);
    _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.Zoom, It.IsAny<CancellationToken>()))
        .ReturnsAsync(connection);
    _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>()))
        .ReturnsAsync("access-token");
    _zoomClient.Setup(z => z.CreateMeetingAsync("access-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ZoomMeetingDto("987654321", "https://us05web.zoom.us/j/987654321", null, "123456"));

    var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.Zoom), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.JoinUrl.Should().Be("https://us05web.zoom.us/j/987654321");
    evt.MeetingLink.Should().Be("https://us05web.zoom.us/j/987654321");
    _meetings.Verify(m => m.AddAsync(
        It.Is<CalendarEventMeeting>(cm => cm.Provider == CalendarEventMeetingProviders.Zoom && cm.ExternalMeetingId == "987654321"),
        It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Handle_ZoomConnectionMissingWriteScope_ReturnsMeetingProviderNotConnectedConflict()
{
    _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(MakeEvent());
    _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.Zoom, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"meeting:read:meeting\"]" // no meeting:write:meeting
        });

    var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.Zoom), CancellationToken.None);

    result.IsSuccess.Should().BeFalse();
    result.Error.Should().Be("meeting_provider_not_connected");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CreateEventMeetingCommandHandlerTests`
Expected: FAIL — compile errors (`CreateEventMeetingCommand` doesn't take a second argument yet, `IZoomMeetingClient` not injected).

- [ ] **Step 3: Update the command record**

```csharp
// src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/CreateEventMeetingCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;

public sealed record CreateEventMeetingResult(string JoinUrl);

/// <summary>Provider is a ONEVO.Domain.Features.Calendar.Entities.CalendarEventMeetingProviders
/// value ("microsoft_teams" | "zoom").</summary>
public sealed record CreateEventMeetingCommand(Guid EventId, string Provider) : IRequest<Result<CreateEventMeetingResult>>;
```

- [ ] **Step 4: Update the handler**

Replace `CreateEventMeetingCommandHandler.cs` in full:

```csharp
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
    IZoomMeetingClient zoomClient,
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

        var isZoom = request.Provider == CalendarEventMeetingProviders.Zoom;
        var externalSource = isZoom ? CalendarExternalSources.Zoom : CalendarExternalSources.OutlookCalendar;
        var oauthProvider = isZoom ? "zoom" : "microsoft";
        var requiredScope = isZoom ? "meeting:write:meeting" : "OnlineMeetings.ReadWrite";

        var connection = await connections.GetByTenantUserProviderAsync(tenantId, currentUser.UserId, externalSource, ct);
        if (connection is null
            || connection.Status != ExternalCalendarConnectionStatuses.Active
            || !HasMeetingScope(connection.ScopesJson, requiredScope))
        {
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");
        }

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return Result<CreateEventMeetingResult>.Conflict("meeting_provider_not_connected");

        string externalMeetingId, joinUrl;
        string? organizerJoinUrl, passcode;
        if (isZoom)
        {
            var dto = await zoomClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct);
            (externalMeetingId, joinUrl, organizerJoinUrl, passcode) = (dto.ExternalMeetingId, dto.JoinUrl, dto.OrganizerJoinUrl, dto.PasscodeOrPin);
        }
        else
        {
            var dto = await teamsClient.CreateMeetingAsync(accessToken, existing.Title, existing.StartDate, existing.EndDate, ct);
            (externalMeetingId, joinUrl, organizerJoinUrl, passcode) = (dto.ExternalMeetingId, dto.JoinUrl, dto.OrganizerJoinUrl, dto.PasscodeOrPin);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            existing.MeetingLink = joinUrl;
            events.Update(existing);

            await meetings.AddAsync(new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = existing.Id,
                ExternalCalendarConnectionId = connection.Id, Provider = request.Provider,
                ExternalMeetingId = externalMeetingId, JoinUrl = joinUrl,
                OrganizerJoinUrl = organizerJoinUrl, PasscodeOrPin = passcode,
                Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
            }, innerCt);

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<CreateEventMeetingResult>.Success(new CreateEventMeetingResult(joinUrl));
        }, ct);
    }

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

- [ ] **Step 5: Update the API contract and controller**

In `CalendarContracts.cs`, add near `CreateEventMeetingResponseModel`:

```csharp
public sealed record CreateEventMeetingRequestModel(string Provider);
```

In `CalendarController.cs`, replace the `CreateMeeting` action (lines 112-120):

```csharp
    [HttpPost("{id:guid}/meeting")]
    [RequirePermission("calendar:write")]
    public async Task<IActionResult> CreateMeeting(Guid id, [FromBody] CreateEventMeetingRequestModel request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateEventMeetingCommand(id, request.Provider), ct);
        return result.IsSuccess
            ? Ok(new CreateEventMeetingResponseModel(result.Value!.JoinUrl))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter CreateEventMeetingCommandHandlerTests`
Expected: PASS (all 6 tests: the original 4 updated to pass `CalendarEventMeetingProviders.MicrosoftTeams` plus the 2 new Zoom ones).

Then run the full suite once to catch any other caller of `CreateEventMeetingCommand`'s old one-arg constructor:
Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/CreateEventMeeting/ src/ONEVO.Api/Controllers/Tenant/Calendar/CalendarController.cs src/ONEVO.Api/Contracts/Calendar/CalendarContracts.cs tests/ONEVO.Tests.Unit/Features/Calendar/CreateEventMeetingCommandHandlerTests.cs
git commit -m "feat(calendar): support creating a Zoom meeting alongside Teams"
```

---

### Task 6: Wire `RemoveEventMeetingCommand` to cancel the right provider

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IZoomMeetingClient` (Task 4). No command signature change needed — `RemoveEventMeetingCommand(EventId)` already loads the `CalendarEventMeeting` row, which carries its own `Provider`.

- [ ] **Step 1: Write the failing test**

In `RemoveEventMeetingCommandHandlerTests.cs`, add a `Mock<IZoomMeetingClient> _zoomClient` and update `BuildSut()`'s constructor call to pass it. Then add (mirroring the existing Teams-cancel test in the same file):

```csharp
[Fact]
public async Task Handle_ZoomMeeting_CancelsViaZoomClient()
{
    var evt = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event", MeetingLink = "https://us05web.zoom.us/j/987654321" };
    var connectionId = Guid.NewGuid();
    var meeting = new CalendarEventMeeting
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, ExternalCalendarConnectionId = connectionId,
        Provider = CalendarEventMeetingProviders.Zoom, ExternalMeetingId = "987654321", JoinUrl = evt.MeetingLink,
        Status = CalendarEventMeetingStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
    };
    var connection = new ExternalCalendarConnection { Id = connectionId, Status = ExternalCalendarConnectionStatuses.Active };
    _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
    _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
    _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
    _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");

    var result = await BuildSut().Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    _zoomClient.Verify(z => z.CancelMeetingAsync("access-token", "987654321", It.IsAny<CancellationToken>()), Times.Once);
    _teamsClient.Verify(t => t.CancelMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter Handle_ZoomMeeting_CancelsViaZoomClient`
Expected: FAIL — compile error (`_zoomClient` field / constructor arg don't exist yet), and even once it compiles the handler would unconditionally call `teamsClient.CancelMeetingAsync`.

- [ ] **Step 3: Update the handler**

Replace `RemoveEventMeetingCommandHandler.cs` in full:

```csharp
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
    IZoomMeetingClient zoomClient,
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

        var isZoom = meeting.Provider == CalendarEventMeetingProviders.Zoom;
        var oauthProvider = isZoom ? "zoom" : "microsoft";
        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, oauthProvider, ct);
        if (accessToken is null)
            return; // reauth required - the remote meeting is orphaned but the local link is still cleared below

        if (isZoom)
            await zoomClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
        else
            await teamsClient.CancelMeetingAsync(accessToken, meeting.ExternalMeetingId, ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter RemoveEventMeetingCommandHandlerTests`
Expected: PASS (all 4 tests: the original 3 unaffected plus the new Zoom one).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Calendar/Commands/RemoveEventMeeting/RemoveEventMeetingCommandHandler.cs tests/ONEVO.Tests.Unit/Features/Calendar/RemoveEventMeetingCommandHandlerTests.cs
git commit -m "feat(calendar): cancel Zoom meetings via the Zoom client, not Teams"
```

---

### Task 7: `ZoomAttendanceSyncJob`

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/Calendar/ZoomAttendanceSyncJob.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:618`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/ZoomAttendanceSyncJobTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingRepositoryTests.cs`

**Interfaces:**
- Consumes: `IZoomMeetingClient` (Task 4).
- Produces: `ICalendarEventMeetingRepository.GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct)` — the existing method gains a `provider` filter so `TeamsAttendanceSyncJob` and `ZoomAttendanceSyncJob` each only pick up their own meetings.

- [ ] **Step 1: Write the failing repository test**

`GetDueForAttendanceSyncAsync` currently has no provider filter — both jobs would otherwise race on the same rows. Open `EfCalendarEventMeetingRepositoryTests.cs`, find the existing test(s) for `GetDueForAttendanceSyncAsync`, and add:

```csharp
[Fact]
public async Task GetDueForAttendanceSyncAsync_FiltersByProvider()
{
    var teamsMeeting = /* build via the same helper the existing due-for-sync test uses, Provider = CalendarEventMeetingProviders.MicrosoftTeams */;
    var zoomMeeting = /* same helper, Provider = CalendarEventMeetingProviders.Zoom */;
    // seed both into the in-memory/test DbContext the way the existing tests in this file do

    var result = await Repository.GetDueForAttendanceSyncAsync(TenantId, CalendarEventMeetingProviders.Zoom, CancellationToken.None);

    var meeting = Assert.Single(result);
    Assert.Equal(zoomMeeting.Id, meeting.Id);
}
```

Match this test's exact setup style (DbContext seeding, `TenantId`/`Repository` field names) to whatever the existing `GetDueForAttendanceSyncAsync` test in the same file already does — read that test first and copy its fixture pattern exactly rather than inventing a new one.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetDueForAttendanceSyncAsync_FiltersByProvider`
Expected: FAIL — compile error, the method doesn't take a `provider` parameter yet.

- [ ] **Step 3: Update the repository interface and implementation**

In `ICalendarEventMeetingRepository.cs`, change the signature:

```csharp
    /// <summary>Active meetings for the given provider whose event has already ended and whose
    /// attendance has never been synced - {Teams,Zoom}AttendanceSyncJob's poll target. The
    /// provider filter keeps the two jobs from racing on each other's rows.</summary>
    Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default);
```

In `EfCalendarEventMeetingRepository.cs`, update the method:

```csharp
    public async Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        var now = _dateTime.UtcNow;
        return await (
            from meeting in _db.CalendarEventMeetings.AsNoTracking()
            join calendarEvent in _db.PersonalCalendarEvents.AsNoTracking() on meeting.CalendarEventId equals calendarEvent.Id
            where meeting.TenantId == tenantId
                && meeting.Provider == provider
                && meeting.Status == CalendarEventMeetingStatuses.Active
                && meeting.LastAttendanceSyncedAt == null
                && calendarEvent.EndDate < now
            select meeting
        ).ToListAsync(ct);
    }
```

- [ ] **Step 4: Update `TeamsAttendanceSyncJob`'s call site to pass its own provider**

In `TeamsAttendanceSyncJob.cs` line 65, change:

```csharp
                    foreach (var meeting in await meetings.GetDueForAttendanceSyncAsync(tenant.Id, CalendarEventMeetingProviders.MicrosoftTeams, ct))
```

- [ ] **Step 5: Run repository + Teams job tests to confirm nothing broke**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "EfCalendarEventMeetingRepositoryTests|TeamsAttendanceSyncJobTests"`
Expected: PASS

- [ ] **Step 6: Write the failing `ZoomAttendanceSyncJob` tests**

Copy `tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs` to `ZoomAttendanceSyncJobTests.cs`, renaming the class to `ZoomAttendanceSyncJobTests`, every `ITeamsMeetingClient`/`_teamsClient` reference to `IZoomMeetingClient`/`_zoomClient`, every `TeamsAttendanceRecordDto` to `ZoomAttendanceRecordDto`, every `"microsoft"` oauth-provider string to `"zoom"`, `CalendarEventMeetingProviders.MicrosoftTeams` to `.Zoom`, and `new TeamsAttendanceSyncJob(...)` to `new ZoomAttendanceSyncJob(...)`. Keep the `MakeMeeting(string? externalMeetingId = null)` helper's distinct-id parameterization (that fixed a real Moq collision bug in the Teams version — see the file's own history) and reuse it identically.

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ZoomAttendanceSyncJobTests`
Expected: FAIL — `ZoomAttendanceSyncJob` doesn't exist yet (compile error).

- [ ] **Step 8: Implement `ZoomAttendanceSyncJob`**

Copy `TeamsAttendanceSyncJob.cs` to `ZoomAttendanceSyncJob.cs` with these changes: class renamed `ZoomAttendanceSyncJob`, `ILogger<ZoomAttendanceSyncJob>`, `ITeamsMeetingClient teamsClient` → `IZoomMeetingClient zoomClient`, the `GetDueForAttendanceSyncAsync` call passes `CalendarEventMeetingProviders.Zoom` as the new second argument, `SyncOneMeetingAsync`'s `tokenProvider.GetFreshAccessTokenAsync(connection, "microsoft", ct)` → `"zoom"`, and `teamsClient.GetAttendanceAsync` → `zoomClient.GetAttendanceAsync`. The full file:

```csharp
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
/// Polls every active tenant's calendar_event_meetings (Provider = "zoom") whose event has ended
/// and whose attendance has never been synced, pulls each one's Zoom past-meeting participant
/// report, and writes calendar_event_meeting_attendances. Mirrors TeamsAttendanceSyncJob exactly
/// (same admin-mode tenant enumeration shape as CalendarSyncJob) - see that class's doc comments
/// for why SetAdminMode() is required first.
/// </summary>
public sealed class ZoomAttendanceSyncJob(IServiceProvider services, ILogger<ZoomAttendanceSyncJob> logger) : BackgroundService
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
            catch (Exception ex) { logger.LogError(ex, "ZoomAttendanceSyncJob run failed."); }
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
                    var zoomClient = scope.ServiceProvider.GetRequiredService<IZoomMeetingClient>();
                    var attendances = scope.ServiceProvider.GetRequiredService<ICalendarEventMeetingAttendanceRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    foreach (var meeting in await meetings.GetDueForAttendanceSyncAsync(tenant.Id, CalendarEventMeetingProviders.Zoom, ct))
                    {
                        try
                        {
                            await SyncOneMeetingAsync(tenant.Id, meeting, connections, tokenProvider, zoomClient, attendances, meetings, unitOfWork, ct);
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
        IZoomMeetingClient zoomClient, ICalendarEventMeetingAttendanceRepository attendances,
        ICalendarEventMeetingRepository meetings, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var connection = await connections.GetByIdForTenantAsync(tenantId, meeting.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return;

        var accessToken = await tokenProvider.GetFreshAccessTokenAsync(connection, "zoom", ct);
        if (accessToken is null)
            return; // reauth required - retried automatically next run once the connection is fixed

        var records = await zoomClient.GetAttendanceAsync(accessToken, meeting.ExternalMeetingId, ct);
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

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ZoomAttendanceSyncJobTests`
Expected: PASS

- [ ] **Step 10: Register the hosted service**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, immediately after line 618 (`services.AddHostedService<Services.Calendar.TeamsAttendanceSyncJob>();`), add:

```csharp
        services.AddHostedService<Services.Calendar.ZoomAttendanceSyncJob>();
```

- [ ] **Step 11: Full backend build + test verification**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --nologo`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --nologo`
Expected: All tests pass (0 failures).

- [ ] **Step 12: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/Calendar/ZoomAttendanceSyncJob.cs src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventMeetingRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventMeetingRepository.cs src/ONEVO.Infrastructure/Services/Calendar/TeamsAttendanceSyncJob.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Calendar/ZoomAttendanceSyncJobTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/EfCalendarEventMeetingRepositoryTests.cs tests/ONEVO.Tests.Unit/Features/Calendar/TeamsAttendanceSyncJobTests.cs
git commit -m "feat(calendar): add ZoomAttendanceSyncJob mirroring the Teams polling job"
```

This is the last backend task. Backend is now fully Zoom-capable end to end.

---

### Task 8: Frontend — `calendar-event-api.service.ts` takes a provider

**Files:**
- Modify: `src/app/modules/calendar/data-access/calendar-event-api.service.ts:59-61`
- Modify: `src/app/modules/calendar/models/calendar-event.model.ts` (if `CreateEventMeetingResponse` or a request type needs it — check first)
- Test: `src/app/modules/calendar/data-access/calendar-event-api.service.spec.ts`

**Interfaces:**
- Produces: `createMeeting(eventId: string, provider: 'microsoft_teams' | 'zoom'): Observable<CreateEventMeetingResponse>` — POST body now `{ provider }`. Consumed by Task 9 (store).

- [ ] **Step 1: Read the existing test for `createMeeting`**

Open `calendar-event-api.service.spec.ts`, find the existing `createMeeting` test, and note its exact `HttpTestingController` assertion style (`expectOne`, `req.request.body`, etc.) to match below.

- [ ] **Step 2: Write the failing test**

Add a test in the same file (matching the existing `createMeeting` test's structure exactly, just asserting the request body includes the provider):

```typescript
it('createMeeting sends the provider in the request body', () => {
  service.createMeeting('event-1', 'zoom').subscribe();
  const req = httpMock.expectOne(`${baseUrl}/event-1/meeting`);
  expect(req.request.method).toBe('POST');
  expect(req.request.body).toEqual({ provider: 'zoom' });
  req.flush({ joinUrl: 'https://us05web.zoom.us/j/123' });
});
```

(Adjust `baseUrl`/`httpMock` variable names to whatever the existing tests in this spec file already use.)

- [ ] **Step 3: Run test to verify it fails**

Run: `npm test -- --include='**/calendar-event-api.service.spec.ts'` (from the frontend repo root)
Expected: FAIL — `createMeeting` currently takes only `eventId` and posts `{}`.

- [ ] **Step 4: Update the service method**

In `calendar-event-api.service.ts`, replace lines 59-61:

```typescript
  createMeeting(eventId: string, provider: 'microsoft_teams' | 'zoom'): Observable<CreateEventMeetingResponse> {
    return this.http.post<CreateEventMeetingResponse>(`${this.baseUrl}/${eventId}/meeting`, { provider });
  }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm test -- --include='**/calendar-event-api.service.spec.ts'`
Expected: PASS (13 tests: the pre-existing 12 plus this new one — the old `createMeeting` test must be updated to also pass a provider argument and assert the new body shape, or it will fail on the call-signature change).

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/calendar/data-access/calendar-event-api.service.ts src/app/modules/calendar/data-access/calendar-event-api.service.spec.ts
git commit -m "feat(calendar): pass meeting provider through the create-meeting API call"
```

---

### Task 9: Frontend — `calendar-event.store.ts` tracks both providers

**Files:**
- Modify: `src/app/modules/calendar/state/calendar-event.store.ts`
- Test: `src/app/modules/calendar/state/calendar-event.store.spec.ts`

**Interfaces:**
- Consumes: Task 8's updated `createMeeting(eventId, provider)`.
- Produces: state fields `hasTeamsCapableConnection: boolean` and `hasZoomCapableConnection: boolean` replace the single `hasMeetingCapableConnection`; `createMeeting(eventId: string, provider: 'microsoft_teams' | 'zoom')`. Consumed by Task 10 (component).

- [ ] **Step 1: Write the failing tests**

In `calendar-event.store.spec.ts`, find the existing `loadMeetingConnectionStatus`/`createMeeting` tests and note their connection-list mock shape. Add:

```typescript
it('loadMeetingConnectionStatus sets hasZoomCapableConnection from an active zoom connection', async () => {
  connectionApiMock.list.and.returnValue(of({
    connections: [{ id: 'c1', provider: 'zoom', status: 'active' }]
  }));
  const store = TestBed.inject(CalendarEventStore);

  await store.loadMeetingConnectionStatus();

  expect(store.hasZoomCapableConnection()).toBe(true);
  expect(store.hasTeamsCapableConnection()).toBe(false);
});

it('createMeeting passes the provider through to the API and updates the event link', async () => {
  apiMock.createMeeting.and.returnValue(of({ joinUrl: 'https://us05web.zoom.us/j/123' }));
  const store = TestBed.inject(CalendarEventStore);
  patchState(store, { events: [{ id: 'e1', meetingLink: null } as any] });

  const success = await store.createMeeting('e1', 'zoom');

  expect(success).toBe(true);
  expect(apiMock.createMeeting).toHaveBeenCalledWith('e1', 'zoom');
  expect(store.events()[0].meetingLink).toBe('https://us05web.zoom.us/j/123');
});
```

(Match `connectionApiMock`/`apiMock`/`patchState` import and mock-setup conventions to whatever the existing tests in this spec file already use — read the file's existing `createMeeting`/`loadMeetingConnectionStatus` tests first.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --include='**/calendar-event.store.spec.ts'`
Expected: FAIL — `hasZoomCapableConnection`/`hasTeamsCapableConnection` don't exist yet, `createMeeting` doesn't accept a provider argument.

- [ ] **Step 3: Update the store**

In `calendar-event.store.ts`, replace the `hasMeetingCapableConnection: boolean;` state field (line 22) with:

```typescript
  readonly hasTeamsCapableConnection: boolean;
  readonly hasZoomCapableConnection: boolean;
```

Update the `withState` initial values (line 31) accordingly:

```typescript
    hasTeamsCapableConnection: false, hasZoomCapableConnection: false, meetingActionSaving: false
```

Replace `loadMeetingConnectionStatus` (lines 159-167):

```typescript
    /** Whether the organizer has an active Microsoft/Zoom connection to host a meeting on -
     * gates the "Add Teams meeting"/"Add Zoom meeting" controls in the event form. A failed
     * fetch leaves both false (same "fail closed, no alarming error" precedent as
     * loadMyTimezone/checkConflicts above). */
    async loadMeetingConnectionStatus(): Promise<void> {
      try {
        const response = await firstValueFrom(connectionApi.list());
        const hasTeams = response.connections.some((c) => c.provider === 'outlook_calendar' && c.status === 'active');
        const hasZoom = response.connections.some((c) => c.provider === 'zoom' && c.status === 'active');
        patchState(store, { hasTeamsCapableConnection: hasTeams, hasZoomCapableConnection: hasZoom });
      } catch {
        patchState(store, { hasTeamsCapableConnection: false, hasZoomCapableConnection: false });
      }
    },
```

Replace `createMeeting` (lines 168-181):

```typescript
    async createMeeting(eventId: string, provider: 'microsoft_teams' | 'zoom'): Promise<boolean> {
      patchState(store, { meetingActionSaving: true, actionError: null });
      try {
        const response = await firstValueFrom(api.createMeeting(eventId, provider));
        patchState(store, {
          meetingActionSaving: false,
          events: store.events().map((e) => (e.id === eventId ? { ...e, meetingLink: response.joinUrl } : e))
        });
        return true;
      } catch (err) {
        patchState(store, { meetingActionSaving: false, actionError: toSafeErrorMessage(err, 'Failed to add the meeting.') });
        return false;
      }
    },
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --include='**/calendar-event.store.spec.ts'`
Expected: PASS (the pre-existing tests referencing `hasMeetingCapableConnection` must be updated to the new field names, or they will fail to compile).

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/calendar/state/calendar-event.store.ts src/app/modules/calendar/state/calendar-event.store.spec.ts
git commit -m "feat(calendar): track Teams and Zoom connection status separately in the store"
```

---

### Task 10: Frontend — event form modal offers both providers

**Files:**
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.ts`
- Modify: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.html:94-110`
- Modify: `src/app/modules/calendar/feature/calendar-page/calendar-page.component.ts` (updates `onAddMeeting`/bindings)
- Modify: `src/app/modules/calendar/feature/calendar-page/calendar-page.component.html`
- Test: `src/app/modules/calendar/ui/calendar-event-form-modal/calendar-event-form-modal.component.spec.ts`

**Interfaces:**
- Consumes: Task 9's `hasTeamsCapableConnection`/`hasZoomCapableConnection`, Task 9's `createMeeting(eventId, provider)`.
- Produces: `@Output() addMeeting = new EventEmitter<'microsoft_teams' | 'zoom'>()` (was `EventEmitter<void>`).

- [ ] **Step 1: Write the failing component tests**

In `calendar-event-form-modal.component.spec.ts`, find the existing test(s) asserting the "+ Add Teams meeting" button click emits `addMeeting`, and the `hasMeetingCapableConnection`-gated visibility tests. Add:

```typescript
it('shows an "Add Zoom meeting" button when hasZoomCapableConnection is true', () => {
  component.hasZoomCapableConnection = true;
  component.event = { ...baseEvent, meetingLink: null };
  fixture.detectChanges();

  const button = fixture.nativeElement.querySelector('[data-testid="calendar-form-add-zoom-meeting"]');
  expect(button).toBeTruthy();
});

it('emits addMeeting with "zoom" when the Add Zoom meeting button is clicked', () => {
  component.hasZoomCapableConnection = true;
  component.event = { ...baseEvent, meetingLink: null };
  fixture.detectChanges();
  const emitted: string[] = [];
  component.addMeeting.subscribe((p: string) => emitted.push(p));

  fixture.nativeElement.querySelector('[data-testid="calendar-form-add-zoom-meeting"]').click();

  expect(emitted).toEqual(['zoom']);
});

it('isAutoGeneratedMeetingLink recognizes a Zoom join URL', () => {
  component.form.controls.meetingLink.setValue('https://us05web.zoom.us/j/987654321?pwd=abc');
  expect(component.isAutoGeneratedMeetingLink()).toBe(true);
});
```

(Match `baseEvent`/`fixture`/`component` setup to whatever this spec file's existing tests already establish in their `beforeEach`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --include='**/calendar-event-form-modal.component.spec.ts'`
Expected: FAIL — `hasZoomCapableConnection` input and the Zoom button don't exist yet; `isAutoGeneratedMeetingLink` doesn't match Zoom URLs.

- [ ] **Step 3: Update the component**

In `calendar-event-form-modal.component.ts`, replace the `hasMeetingCapableConnection` input (lines 100-102):

```typescript
  /** Whether the organizer has an active Microsoft connection that can host a Teams meeting. */
  @Input() hasTeamsCapableConnection = false;
  /** Whether the organizer has an active Zoom connection that can host a Zoom meeting. */
  @Input() hasZoomCapableConnection = false;
```

Replace the `addMeeting` output (line 109):

```typescript
  @Output() addMeeting = new EventEmitter<'microsoft_teams' | 'zoom'>();
```

Replace `isAutoGeneratedMeetingLink` (lines 359-365):

```typescript
  /** An auto-generated join link is always a real teams.microsoft.com or *.zoom.us URL - using
   * that as the "was this attached via Add Teams/Zoom meeting, not typed in" signal avoids
   * needing a separate flag threaded through the event API just for this display choice. */
  isAutoGeneratedMeetingLink(): boolean {
    const value = this.form.controls.meetingLink.value ?? '';
    return /^https:\/\/teams\.microsoft\.com\//i.test(value) || /^https:\/\/([a-z0-9-]+\.)?zoom\.us\//i.test(value);
  }
```

Replace `onAddMeeting` (lines 367-369):

```typescript
  onAddMeeting(provider: 'microsoft_teams' | 'zoom'): void {
    this.addMeeting.emit(provider);
  }
```

- [ ] **Step 4: Update the template**

In `calendar-event-form-modal.component.html`, replace lines 94-110:

```html
            @if (isEdit) {
              @if (hasTeamsCapableConnection) {
                <button
                  type="button"
                  class="-mt-1 self-start text-xs font-medium text-[var(--color-accent)] hover:underline disabled:opacity-50"
                  [disabled]="meetingActionSaving"
                  (click)="onAddMeeting('microsoft_teams')"
                  data-testid="calendar-form-add-teams-meeting"
                >
                  + Add Teams meeting
                </button>
              }
              @if (hasZoomCapableConnection) {
                <button
                  type="button"
                  class="-mt-1 self-start text-xs font-medium text-[var(--color-accent)] hover:underline disabled:opacity-50"
                  [disabled]="meetingActionSaving"
                  (click)="onAddMeeting('zoom')"
                  data-testid="calendar-form-add-zoom-meeting"
                >
                  + Add Zoom meeting
                </button>
              }
              @if (!hasTeamsCapableConnection && !hasZoomCapableConnection) {
                <a routerLink="/settings/calendar-connections" class="-mt-1 text-xs text-[var(--color-text-secondary)] hover:underline" data-testid="calendar-form-connect-microsoft-prompt">
                  Connect Microsoft or Zoom to add a meeting
                </a>
              }
            }
```

- [ ] **Step 5: Update the parent page component**

In `calendar-page.component.ts`, find `onAddMeeting()` and change its signature to accept and forward the provider:

```typescript
  async onAddMeeting(eventId: string, provider: 'microsoft_teams' | 'zoom'): Promise<void> {
    await this.store.createMeeting(eventId, provider);
  }
```

In `calendar-page.component.html`, update the binding on `<app-calendar-event-form-modal>`:

```html
  [hasTeamsCapableConnection]="store.hasTeamsCapableConnection()"
  [hasZoomCapableConnection]="store.hasZoomCapableConnection()"
  (addMeeting)="onAddMeeting(selectedEvent()!.id, $event)"
```

(Match the exact existing binding syntax/event-id source already used for `(removeMeeting)` in this template — read the surrounding lines first and follow the same pattern, since the exact expression for "the currently open event's id" depends on how this component already tracks it.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `npm test -- --include='**/calendar-event-form-modal.component.spec.ts'`
Expected: PASS (every pre-existing test referencing `hasMeetingCapableConnection` or the old void-emitting `addMeeting` must be updated to the new field/signature, or they will fail to compile).

Then run the full frontend test suite once to catch any other consumer:
Run: `npm test -- --watch=false`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/calendar/ui/calendar-event-form-modal/ src/app/modules/calendar/feature/calendar-page/
git commit -m "feat(calendar): offer Zoom meetings alongside Teams in the event form"
```

---

## Self-Review Notes

- **Spec coverage**: Scope items 1-4 (extend `external_calendar_connections`, `CreateEventMeetingCommand`/`RemoveEventMeetingCommand` Zoom support, background attendance polling, event-form UI) are covered by Tasks 2-3 (connection), 5-6 (create/remove), 7 (polling job), 8-10 (UI). The spec's "Blocked on: a real Zoom Marketplace OAuth app" is resolved (app created, scopes verified) — this plan assumes the operator still needs to enter the Client ID/Secret into `platform_oauth_apps` via the existing DevPlatform admin UI before any of this is live-testable; that data-entry step is outside this plan's scope (no code change) and should happen before or during Task 1's verification.
- **Explicitly out of scope, unchanged from the spec**: recurring-meeting series, wiring attendance into the Work Pattern card's Meeting-time metric.
- **Correctness fix folded in**: Task 2 also fixes a pre-existing bug where `CompleteCalendarConnectionCommandHandler` miscategorized any non-Google provider (including a hypothetical Zoom callback) as Outlook — this was latent because Zoom wasn't wired into the callback route before this plan.
- **Type consistency**: `CreateEventMeetingCommand(Guid EventId, string Provider)` (Task 5) is the only signature change to an existing type; every later task (6 doesn't need it since it reads `meeting.Provider` from the DB row) and the frontend (Tasks 8-10) consistently thread `'microsoft_teams' | 'zoom'` string literals matching `CalendarEventMeetingProviders`' actual values.
