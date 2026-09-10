# Zoom Integration — Connection (Backend, Plan 1 of 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a tenant user connect and disconnect their own Zoom account through the platform (per-user OAuth), storing encrypted tokens, so later plans can create Zoom meetings as that user.

**Architecture:** Reuse the existing generic tenant/user integration framework — `UserIntegrationConnection` rows keyed by `integrationKey = "zoom"`, `platform_oauth_apps` provider `zoom`, `integration_catalog`, `IOAuthStateProtector`, `IEncryptionService`. This plan adds a Zoom-specific OAuth token client, an availability helper, five MediatR requests, and a controller — mirroring the existing GitHub integration (`Features/SharedPlatform/TenantIntegrations/**`, `Controllers/Tenant/Integrations/GitHubIntegrationController.cs`). The upsert and disconnect commands are already provider-generic and are reused unchanged.

**Tech Stack:** .NET 10, C# 13, MediatR, EF Core (PostgreSQL), `Result`/`Result<T>` (`ONEVO.Application.Common.Models`), xUnit + NSubstitute (`tests/ONEVO.Tests.Unit`), `System.Net.Http.Json`, `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-09-10-zoom-calendar-meeting-scheduling-design.md`

## Global Constraints

- **No architecture rename.** The shared OAuth-state record keeps its historical name `GitHubOAuthState` and is reused for Zoom as-is (all its fields are provider-agnostic; the `Provider`/`IntegrationKey` fields distinguish). The design doc's "rename to `OAuthConnectState`" is deferred cleanup, out of scope here — do **not** touch GitHub code.
- **Integration key / provider slug:** exactly `"zoom"` (lowercase) everywhere.
- **OAuth scopes requested at authorize time:** exactly `"user:read"`, `"meeting:read"`, `"meeting:write"` (space-joined in that order). Do **not** widen `PlatformOAuthProviderCatalog`'s `zoom` `DefaultScopes` — request the wider set from `ZoomUserOAuthRules` instead.
- **Zoom token endpoint auth:** HTTP Basic header `Authorization: Basic base64(clientId:clientSecret)`, body `application/x-www-form-urlencoded`. Not client_id/client_secret in the body (that is the GitHub style — Zoom differs).
- **Zoom refresh tokens rotate on every refresh.** Always persist the new `refresh_token` from a refresh response; never keep the old one when a new one is returned.
- **Token storage:** `IEncryptionService.Encrypt(string) -> string` / `.Decrypt(string) -> string`; the `UserIntegrationConnection.*Encrypted` columns are `string?`. Never log or serialize a decrypted token or a client secret.
- **Command + handler in one file** named `<Name>Command.cs` / `<Name>Query.cs`, matching the GitHub files.
- **Result status codes:** reuse the GitHub handlers' conventions — `422` for misconfiguration (catalog inactive, no OAuth app), `502` for a failed provider HTTP call, `400` for bad input, `404`/`NotFound` for "not connected".
- **Commit after every task** (each task ends green: `dotnet build` + the task's tests pass).
- Run tests with: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~<ClassName>"`.

## Operator setup (prerequisite — NOT a code task)

Before this integration works in a real environment, an operator must, via the DevPlatform admin app:

1. Configure the **Zoom OAuth app** (`platform_oauth_apps`, provider `zoom`) — client id + client secret from a Zoom Marketplace **User-managed OAuth** app whose redirect URL is `{apiBaseUrl}/api/v1/integrations/zoom/connect/callback` and whose scopes include `user:read`, `meeting:read`, `meeting:write`.
2. Create the **`zoom` integration_catalog entry** (`POST` create-integration) with `connection_scope = "user"`, `onevo_app_provider = "zoom"`, `is_active = true`, and link it to the `calendar` module.

Task 8 seeds both automatically for local/dev/test environments only.

## File structure

| File | Responsibility |
| --- | --- |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/OAuthReturnUrlRules.cs` | **New.** Shared safe-relative-path validator for OAuth `returnUrl`. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/ZoomUserOAuthRules.cs` | **New.** Zoom integration key/provider constants, required scopes, authorize-URL builder, returnUrl validation. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/ZoomUserIntegrationAvailability.cs` | **New.** Resolves the active Zoom `platform_oauth_apps` config after checking the catalog entry is active, provider-matched, and user-scoped. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/ServiceInterfaces/IZoomOAuthClient.cs` | **New.** Zoom OAuth HTTP contract (exchange code, refresh, get current user) + request/result records. |
| `src/ONEVO.Infrastructure/ExternalServices/Zoom/ZoomOAuthTokenClient.cs` | **New.** `IZoomOAuthClient` implementation over `HttpClient` (Basic-auth token endpoint, `api.zoom.us/v2/users/me`). |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/DTOs/Responses/ZoomOAuthResponses.cs` | **New.** `ZoomOAuthStartResponse`, `ZoomOAuthCompleteResponse`. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/StartZoomUserOAuth/StartZoomUserOAuthCommand.cs` | **New.** Build the signed Zoom authorize URL. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/CompleteZoomUserOAuth/CompleteZoomUserOAuthCommand.cs` | **New.** Validate state, exchange code, fetch profile, upsert the connection (via the generic upsert command). |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Queries/GetZoomUserIntegrationStatus/GetZoomUserIntegrationStatusQuery.cs` | **New.** Return the caller's Zoom connection status DTO. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/RefreshOwnZoomConnection/RefreshOwnZoomConnectionCommand.cs` | **New.** Refresh the caller's Zoom token, persisting the rotated refresh token. |
| `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/DTOs/Requests/StartZoomOAuthRequest.cs` | **New.** `{ returnUrl }` request body for `connect/start`. |
| `src/ONEVO.Api/Controllers/Tenant/Integrations/ZoomIntegrationController.cs` | **New.** `api/v1/integrations/zoom` — start / callback / status / disconnect / refresh. |
| `src/ONEVO.Application/DependencyInjection.cs` | **Modify** (~line 77) — `AddScoped<ZoomUserIntegrationAvailability>()`. |
| `src/ONEVO.Infrastructure/DependencyInjection.cs` | **Modify** (~line 445) — `AddHttpClient<IZoomOAuthClient, ZoomOAuthTokenClient>`. |
| `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` | **Modify** — add `SeedZoomIntegrationCatalogAsync` mirroring `SeedGitHubIntegrationCatalogAsync`. |
| `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/Zoom*Tests.cs` | **New.** Unit tests per task. |

---

### Task 1: `OAuthReturnUrlRules` + `ZoomUserOAuthRules`

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/OAuthReturnUrlRules.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/ZoomUserOAuthRules.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthRulesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `OAuthReturnUrlRules.Validate(string? returnUrl) -> string?` — returns the trimmed value if it is a safe same-site relative path (`starts with "/"`, not `"//"`, no `"\"`); otherwise `null`.
  - `ZoomUserOAuthRules.IntegrationKey` = `"zoom"`, `.Provider` = `"zoom"`.
  - `ZoomUserOAuthRules.RequiredScopes` — `IReadOnlyList<string>` = `["user:read", "meeting:read", "meeting:write"]`.
  - `ZoomUserOAuthRules.ValidateReturnUrl(string? returnUrl) -> string?` — delegates to `OAuthReturnUrlRules.Validate`.
  - `ZoomUserOAuthRules.BuildAuthorizationUrl(string authorizationUrl, string clientId, string redirectUri, string state) -> string` — appends `response_type=code&client_id=…&redirect_uri=…&scope=<space-joined RequiredScopes>&state=…`, each value `Uri.EscapeDataString`-encoded, using `?` or `&` depending on whether `authorizationUrl` already contains `?`.

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed class ZoomUserOAuthRulesTests
{
    [Fact]
    public void BuildAuthorizationUrl_includes_encoded_params_and_all_required_scopes()
    {
        var url = ZoomUserOAuthRules.BuildAuthorizationUrl(
            "https://zoom.us/oauth/authorize",
            "client-123",
            "https://tenant.test/api/v1/integrations/zoom/connect/callback",
            "protected-state");

        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=client-123", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Ftenant.test%2Fapi%2Fv1%2Fintegrations%2Fzoom%2Fconnect%2Fcallback", url);
        Assert.Contains("scope=user%3Aread%20meeting%3Aread%20meeting%3Awrite", url);
        Assert.Contains("state=protected-state", url);
        Assert.DoesNotContain("client_secret", url, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/settings/integrations", "/settings/integrations")]
    [InlineData("  /a  ", "/a")]
    [InlineData("//evil.example", null)]
    [InlineData("https://evil.example", null)]
    [InlineData("/a\\b", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ValidateReturnUrl_only_accepts_safe_relative_paths(string? input, string? expected)
    {
        Assert.Equal(expected, ZoomUserOAuthRules.ValidateReturnUrl(input));
    }

    [Fact]
    public void RequiredScopes_are_exactly_the_three_documented_scopes_in_order()
    {
        Assert.Equal(new[] { "user:read", "meeting:read", "meeting:write" }, ZoomUserOAuthRules.RequiredScopes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthRulesTests"`
Expected: FAIL — `ZoomUserOAuthRules` / `OAuthReturnUrlRules` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`OAuthReturnUrlRules.cs`:

```csharp
namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

/// <summary>Shared validator for OAuth connect-flow return URLs: only same-site relative paths.</summary>
public static class OAuthReturnUrlRules
{
    public static string? Validate(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var value = returnUrl.Trim();
        if (!value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\'))
        {
            return null;
        }

        return value;
    }
}
```

`ZoomUserOAuthRules.cs`:

```csharp
namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

public static class ZoomUserOAuthRules
{
    public const string IntegrationKey = "zoom";
    public const string Provider = "zoom";

    /// <summary>Requested at authorize time. Wider than the platform catalog default (meeting:read).</summary>
    public static readonly IReadOnlyList<string> RequiredScopes = new[] { "user:read", "meeting:read", "meeting:write" };

    public static string? ValidateReturnUrl(string? returnUrl) => OAuthReturnUrlRules.Validate(returnUrl);

    public static string BuildAuthorizationUrl(
        string authorizationUrl,
        string clientId,
        string redirectUri,
        string state)
    {
        var separator = authorizationUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return authorizationUrl + separator +
            "response_type=code" +
            "&client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
            "&scope=" + Uri.EscapeDataString(string.Join(' ', RequiredScopes)) +
            "&state=" + Uri.EscapeDataString(state);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthRulesTests"`
Expected: PASS (4 test cases across the theory + 2 facts).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/OAuthReturnUrlRules.cs \
        src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/ZoomUserOAuthRules.cs \
        tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthRulesTests.cs
git commit -m "feat(zoom): add ZoomUserOAuthRules and shared OAuthReturnUrlRules"
```

---

### Task 2: `IZoomOAuthClient` + `ZoomOAuthTokenClient`

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/ServiceInterfaces/IZoomOAuthClient.cs`
- Create: `src/ONEVO.Infrastructure/ExternalServices/Zoom/ZoomOAuthTokenClient.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (immediately after the `AddHttpClient<IGitHubOAuthClient, GitHubOAuthTokenClient>` block, ~line 448)
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomOAuthTokenClientTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `record ZoomOAuthTokenRequest(string TokenUrl, string ClientId, string ClientSecret, string Code, string RedirectUri)`
  - `record ZoomOAuthRefreshRequest(string TokenUrl, string ClientId, string ClientSecret, string RefreshToken)`
  - `record ZoomOAuthTokenResult(string AccessToken, string? RefreshToken, long? ExpiresInSeconds, string? Scope, string? TokenType)`
  - `record ZoomUserProfileResult(string ProviderUserId, string? DisplayName, string? Email)`
  - `interface IZoomOAuthClient { Task<ZoomOAuthTokenResult?> ExchangeCodeAsync(ZoomOAuthTokenRequest, CancellationToken); Task<ZoomOAuthTokenResult?> RefreshTokenAsync(ZoomOAuthRefreshRequest, CancellationToken); Task<ZoomUserProfileResult?> GetCurrentUserAsync(string accessToken, CancellationToken); }`
  - Each method returns `null` on any non-2xx / unparseable response.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Text;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Zoom;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed class ZoomOAuthTokenClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpResponseMessage Response { get; set; } =
            new(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return Response;
        }
    }

    private static ZoomOAuthTokenClient ClientWith(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.zoom.us/") });

    [Fact]
    public async Task ExchangeCodeAsync_posts_basic_auth_and_form_body_and_parses_tokens()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3599,\"scope\":\"user:read meeting:write\",\"token_type\":\"bearer\"}")
            }
        };
        var client = ClientWith(handler);

        var result = await client.ExchangeCodeAsync(
            new ZoomOAuthTokenRequest("https://zoom.us/oauth/token", "cid", "secret", "the-code", "https://t.test/cb"),
            default);

        Assert.NotNull(result);
        Assert.Equal("at", result!.AccessToken);
        Assert.Equal("rt", result.RefreshToken);
        Assert.Equal(3599, result.ExpiresInSeconds);

        var auth = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal("cid:secret", Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)));
        Assert.Contains("grant_type=authorization_code", handler.LastBody);
        Assert.Contains("code=the-code", handler.LastBody);
        Assert.Contains("redirect_uri=https%3A%2F%2Ft.test%2Fcb", handler.LastBody);
        Assert.DoesNotContain("client_secret", handler.LastBody!);
    }

    [Fact]
    public async Task RefreshTokenAsync_sends_refresh_grant_and_returns_null_on_error_status()
    {
        var handler = new StubHandler { Response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") } };
        var client = ClientWith(handler);

        var result = await client.RefreshTokenAsync(
            new ZoomOAuthRefreshRequest("https://zoom.us/oauth/token", "cid", "secret", "old-rt"),
            default);

        Assert.Null(result);
        Assert.Contains("grant_type=refresh_token", handler.LastBody);
        Assert.Contains("refresh_token=old-rt", handler.LastBody);
    }

    [Fact]
    public async Task GetCurrentUserAsync_parses_id_and_email()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"abc123\",\"first_name\":\"A\",\"last_name\":\"B\",\"email\":\"a@b.test\"}")
            }
        };
        var client = ClientWith(handler);

        var profile = await client.GetCurrentUserAsync("at", default);

        Assert.NotNull(profile);
        Assert.Equal("abc123", profile!.ProviderUserId);
        Assert.Equal("a@b.test", profile.Email);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomOAuthTokenClientTests"`
Expected: FAIL — types missing (compile error).

- [ ] **Step 3: Write minimal implementation**

`IZoomOAuthClient.cs`:

```csharp
namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

public sealed record ZoomOAuthTokenRequest(string TokenUrl, string ClientId, string ClientSecret, string Code, string RedirectUri);
public sealed record ZoomOAuthRefreshRequest(string TokenUrl, string ClientId, string ClientSecret, string RefreshToken);
public sealed record ZoomOAuthTokenResult(string AccessToken, string? RefreshToken, long? ExpiresInSeconds, string? Scope, string? TokenType);
public sealed record ZoomUserProfileResult(string ProviderUserId, string? DisplayName, string? Email);

public interface IZoomOAuthClient
{
    Task<ZoomOAuthTokenResult?> ExchangeCodeAsync(ZoomOAuthTokenRequest request, CancellationToken ct);
    Task<ZoomOAuthTokenResult?> RefreshTokenAsync(ZoomOAuthRefreshRequest request, CancellationToken ct);
    Task<ZoomUserProfileResult?> GetCurrentUserAsync(string accessToken, CancellationToken ct);
}
```

`ZoomOAuthTokenClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Zoom;

public sealed class ZoomOAuthTokenClient : IZoomOAuthClient
{
    private readonly HttpClient _httpClient;

    public ZoomOAuthTokenClient(HttpClient httpClient) => _httpClient = httpClient;

    public Task<ZoomOAuthTokenResult?> ExchangeCodeAsync(ZoomOAuthTokenRequest request, CancellationToken ct)
        => PostTokenAsync(request.TokenUrl, request.ClientId, request.ClientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = request.Code,
            ["redirect_uri"] = request.RedirectUri
        }, ct);

    public Task<ZoomOAuthTokenResult?> RefreshTokenAsync(ZoomOAuthRefreshRequest request, CancellationToken ct)
        => PostTokenAsync(request.TokenUrl, request.ClientId, request.ClientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = request.RefreshToken
        }, ct);

    private async Task<ZoomOAuthTokenResult?> PostTokenAsync(
        string tokenUrl, string clientId, string clientSecret, Dictionary<string, string> form, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var accessToken = GetString(root, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        return new ZoomOAuthTokenResult(
            accessToken,
            GetString(root, "refresh_token"),
            root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt64(out var seconds) ? seconds : null,
            GetString(root, "scope"),
            GetString(root, "token_type"));
    }

    public async Task<ZoomUserProfileResult?> GetCurrentUserAsync(string accessToken, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://api.zoom.us/v2/users/me");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var id = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var displayName = string.Join(' ', new[] { GetString(root, "first_name"), GetString(root, "last_name") }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return new ZoomUserProfileResult(id, string.IsNullOrWhiteSpace(displayName) ? null : displayName, GetString(root, "email"));
    }

    private static string? GetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
```

DI (`src/ONEVO.Infrastructure/DependencyInjection.cs`, right after the GitHub `AddHttpClient` block):

```csharp
        services.AddHttpClient<IZoomOAuthClient, ONEVO.Infrastructure.ExternalServices.Zoom.ZoomOAuthTokenClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
```

Add `using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;` to that file if it is not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomOAuthTokenClientTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/ServiceInterfaces/IZoomOAuthClient.cs \
        src/ONEVO.Infrastructure/ExternalServices/Zoom/ZoomOAuthTokenClient.cs \
        src/ONEVO.Infrastructure/DependencyInjection.cs \
        tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomOAuthTokenClientTests.cs
git commit -m "feat(zoom): add IZoomOAuthClient and ZoomOAuthTokenClient (Basic-auth token endpoint)"
```

---

### Task 3: `ZoomUserIntegrationAvailability`

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/ZoomUserIntegrationAvailability.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs` (next to `services.AddScoped<GitHubUserIntegrationAvailability>();`, ~line 77)
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserIntegrationAvailabilityTests.cs`

**Interfaces:**
- Consumes: `ZoomUserOAuthRules` (Task 1); `IIntegrationCatalogRepository.GetByKeyAsync(string, CancellationToken) -> IntegrationCatalogEntry?`; `IPlatformOAuthAppResolver.GetActiveAppForProviderAsync(string, CancellationToken) -> ResolvedPlatformOAuthApp?`.
- Produces:
  - `sealed class ZoomUserIntegrationAvailability` with `Task<Result<ResolvedPlatformOAuthApp>> ValidateAsync(Guid tenantId, CancellationToken ct)`.
  - Failure cases: catalog entry missing/inactive → `422`; `OnevoAppProvider != "zoom"` → `422`; `ConnectionScope` not `"user"`/`"both"` → `422`; no active OAuth app → `422`. Success → the `ResolvedPlatformOAuthApp` (`.ClientId`, `.AuthorizationUrl`, `.TokenUrl`).
  - Note: unlike GitHub, there is **no** tenant-approval row check and **no** module-entitlement gate for Zoom user connections in Phase 1.

- [ ] **Step 1: Write the failing test**

```csharp
using NSubstitute;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed class ZoomUserIntegrationAvailabilityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static IntegrationCatalogEntry Entry(bool isActive = true, string provider = "zoom", string scope = "user") => new()
    {
        IntegrationKey = "zoom", DisplayName = "Zoom", ConnectionScope = scope,
        OnevoAppProvider = provider, IsActive = isActive, CreatedById = Guid.NewGuid()
    };

    private static ResolvedPlatformOAuthApp App() =>
        new("zoom", "client-1", "https://zoom.us/oauth/authorize", "https://zoom.us/oauth/token", new[] { "meeting:read" });

    private static ZoomUserIntegrationAvailability Build(
        IntegrationCatalogEntry? entry, ResolvedPlatformOAuthApp? app)
    {
        var catalog = Substitute.For<IIntegrationCatalogRepository>();
        catalog.GetByKeyAsync("zoom", Arg.Any<CancellationToken>()).Returns(entry);
        var apps = Substitute.For<IPlatformOAuthAppResolver>();
        apps.GetActiveAppForProviderAsync("zoom", Arg.Any<CancellationToken>()).Returns(app);
        return new ZoomUserIntegrationAvailability(catalog, apps);
    }

    [Fact]
    public async Task Succeeds_when_catalog_active_user_scoped_and_app_present()
    {
        var result = await Build(Entry(), App()).ValidateAsync(TenantId, default);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("client-1", result.Value!.ClientId);
    }

    [Fact]
    public async Task Fails_422_when_catalog_entry_inactive()
    {
        var result = await Build(Entry(isActive: false), App()).ValidateAsync(TenantId, default);
        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Fails_422_when_scope_is_tenant_only()
    {
        var result = await Build(Entry(scope: "tenant"), App()).ValidateAsync(TenantId, default);
        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Fails_422_when_no_active_oauth_app()
    {
        var result = await Build(Entry(), app: null).ValidateAsync(TenantId, default);
        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserIntegrationAvailabilityTests"`
Expected: FAIL — `ZoomUserIntegrationAvailability` missing.

- [ ] **Step 3: Write minimal implementation**

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

public sealed class ZoomUserIntegrationAvailability
{
    private readonly IIntegrationCatalogRepository _catalog;
    private readonly IPlatformOAuthAppResolver _oauthApps;

    public ZoomUserIntegrationAvailability(IIntegrationCatalogRepository catalog, IPlatformOAuthAppResolver oauthApps)
    {
        _catalog = catalog;
        _oauthApps = oauthApps;
    }

    public async Task<Result<ResolvedPlatformOAuthApp>> ValidateAsync(Guid tenantId, CancellationToken ct)
    {
        var integration = await _catalog.GetByKeyAsync(ZoomUserOAuthRules.IntegrationKey, ct);
        if (integration is null || !integration.IsActive)
        {
            return Result<ResolvedPlatformOAuthApp>.Failure("Zoom integration is not available.", 422);
        }

        if (!string.Equals(integration.OnevoAppProvider, ZoomUserOAuthRules.Provider, StringComparison.Ordinal))
        {
            return Result<ResolvedPlatformOAuthApp>.Failure("Zoom OAuth configuration is unavailable.", 422);
        }

        if (integration.ConnectionScope is not "user" and not "both")
        {
            return Result<ResolvedPlatformOAuthApp>.Failure("Zoom is not configured for user connections.", 422);
        }

        var app = await _oauthApps.GetActiveAppForProviderAsync(ZoomUserOAuthRules.Provider, ct);
        if (app is null)
        {
            return Result<ResolvedPlatformOAuthApp>.Failure("Zoom OAuth configuration is unavailable.", 422);
        }

        return Result<ResolvedPlatformOAuthApp>.Success(app);
    }
}
```

DI (`src/ONEVO.Application/DependencyInjection.cs`, beside the GitHub line):

```csharp
                services.AddScoped<ZoomUserIntegrationAvailability>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserIntegrationAvailabilityTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Helpers/ZoomUserIntegrationAvailability.cs \
        src/ONEVO.Application/DependencyInjection.cs \
        tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserIntegrationAvailabilityTests.cs
git commit -m "feat(zoom): add ZoomUserIntegrationAvailability resolver"
```

---

### Task 4: `StartZoomUserOAuthCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/DTOs/Responses/ZoomOAuthResponses.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/StartZoomUserOAuth/StartZoomUserOAuthCommand.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthTests.cs`

**Interfaces:**
- Consumes: `ZoomUserOAuthRules` (Task 1); `ZoomUserIntegrationAvailability` (Task 3); `IOAuthStateProtector.Protect(GitHubOAuthState) -> string`; `GitHubOAuthState` record (existing, reused); `ICurrentUser` (`.TenantId`, `.UserId`, `.SessionBinding`, `.IsAuthenticated`).
- Produces:
  - `record ZoomOAuthStartResponse(string AuthorizationUrl, DateTimeOffset ExpiresAt)`
  - `record StartZoomUserOAuthCommand(string? ReturnUrl, string RedirectUri) : IRequest<Result<ZoomOAuthStartResponse>>`
  - `class StartZoomUserOAuthCommandHandler` — validates returnUrl, calls availability, builds a 10-minute signed `GitHubOAuthState` (fields: nonce, tenantId, userId, `ZoomUserOAuthRules.IntegrationKey`, `ZoomUserOAuthRules.Provider`, returnUrl, issuedAt, expiresAt, `currentUser.SessionBinding`), returns the authorize URL.

- [ ] **Step 1: Write the failing test**

```csharp
using NSubstitute;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartZoomUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed partial class ZoomUserOAuthTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ICurrentUser CurrentUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.TenantId.Returns(TenantId);
        user.UserId.Returns(UserId);
        user.SessionBinding.Returns("session-1");
        return user;
    }

    private static ZoomUserIntegrationAvailability Availability(bool isActive = true, string scope = "user")
    {
        var catalog = Substitute.For<IIntegrationCatalogRepository>();
        catalog.GetByKeyAsync("zoom", Arg.Any<CancellationToken>()).Returns(new IntegrationCatalogEntry
        {
            IntegrationKey = "zoom", DisplayName = "Zoom", ConnectionScope = scope,
            OnevoAppProvider = "zoom", IsActive = isActive, CreatedById = Guid.NewGuid()
        });
        var apps = Substitute.For<IPlatformOAuthAppResolver>();
        apps.GetActiveAppForProviderAsync("zoom", Arg.Any<CancellationToken>()).Returns(
            new ResolvedPlatformOAuthApp("zoom", "client-id", "https://zoom.us/oauth/authorize", "https://zoom.us/oauth/token", new[] { "meeting:read" }));
        return new ZoomUserIntegrationAvailability(catalog, apps);
    }

    [Fact]
    public async Task Start_builds_safe_authorization_url()
    {
        var protector = Substitute.For<IOAuthStateProtector>();
        protector.Protect(Arg.Any<ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces.GitHubOAuthState>())
            .Returns("protected-state");
        var handler = new StartZoomUserOAuthCommandHandler(CurrentUser(), Availability(), protector);

        var result = await handler.Handle(
            new StartZoomUserOAuthCommand("/settings/integrations", "https://t.test/api/v1/integrations/zoom/connect/callback"),
            default);

        Assert.True(result.IsSuccess, result.Error);
        var url = result.Value!.AuthorizationUrl;
        Assert.Contains("client_id=client-id", url);
        Assert.Contains("scope=user%3Aread%20meeting%3Aread%20meeting%3Awrite", url);
        Assert.Contains("state=protected-state", url);
        Assert.DoesNotContain("client_secret", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_fails_422_when_catalog_inactive()
    {
        var handler = new StartZoomUserOAuthCommandHandler(
            CurrentUser(), Availability(isActive: false), Substitute.For<IOAuthStateProtector>());

        var result = await handler.Handle(new StartZoomUserOAuthCommand(null, "https://t.test/cb"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Start_rejects_unsafe_return_url()
    {
        var handler = new StartZoomUserOAuthCommandHandler(
            CurrentUser(), Availability(), Substitute.For<IOAuthStateProtector>());

        var result = await handler.Handle(new StartZoomUserOAuthCommand("https://evil.example", "https://t.test/cb"), default);

        Assert.False(result.IsSuccess);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthTests"`
Expected: FAIL — `StartZoomUserOAuthCommand` / `ZoomOAuthStartResponse` missing.

- [ ] **Step 3: Write minimal implementation**

`ZoomOAuthResponses.cs`:

```csharp
namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;

public sealed record ZoomOAuthStartResponse(string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record ZoomOAuthCompleteResponse(string IntegrationKey, string Status, string? ProviderEmail, string? ReturnUrl);
```

`StartZoomUserOAuthCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartZoomUserOAuth;

public sealed record StartZoomUserOAuthCommand(string? ReturnUrl, string RedirectUri)
    : IRequest<Result<ZoomOAuthStartResponse>>;

public sealed class StartZoomUserOAuthCommandHandler
    : IRequestHandler<StartZoomUserOAuthCommand, Result<ZoomOAuthStartResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ZoomUserIntegrationAvailability _availability;
    private readonly IOAuthStateProtector _stateProtector;

    public StartZoomUserOAuthCommandHandler(
        ICurrentUser currentUser,
        ZoomUserIntegrationAvailability availability,
        IOAuthStateProtector stateProtector)
    {
        _currentUser = currentUser;
        _availability = availability;
        _stateProtector = stateProtector;
    }

    public async Task<Result<ZoomOAuthStartResponse>> Handle(StartZoomUserOAuthCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Result<ZoomOAuthStartResponse>.Forbidden();
        }

        var returnUrl = ZoomUserOAuthRules.ValidateReturnUrl(request.ReturnUrl);
        if (!string.IsNullOrWhiteSpace(request.ReturnUrl) && returnUrl is null)
        {
            return Result<ZoomOAuthStartResponse>.Failure("Return URL must be a safe local path.");
        }

        var availability = await _availability.ValidateAsync(_currentUser.TenantId, cancellationToken);
        if (!availability.IsSuccess)
        {
            return Result<ZoomOAuthStartResponse>.Failure(
                availability.Error ?? "Zoom integration is unavailable.",
                availability.StatusCode ?? 400);
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(10);
        var payload = new GitHubOAuthState(
            Guid.NewGuid().ToString("N"),
            _currentUser.TenantId,
            _currentUser.UserId,
            ZoomUserOAuthRules.IntegrationKey,
            ZoomUserOAuthRules.Provider,
            returnUrl,
            issuedAt,
            expiresAt,
            _currentUser.SessionBinding);
        var state = _stateProtector.Protect(payload);
        var app = availability.Value!;
        var authorizationUrl = ZoomUserOAuthRules.BuildAuthorizationUrl(
            app.AuthorizationUrl, app.ClientId, request.RedirectUri, state);

        return Result<ZoomOAuthStartResponse>.Success(new ZoomOAuthStartResponse(authorizationUrl, expiresAt));
    }
}
```

> Note: `ZoomUserOAuthTests.cs` is declared `partial` because Tasks 4, 5, and 6 all add methods to it. Each task adds a `public sealed partial class ZoomUserOAuthTests` block in the same file, or split into three files `ZoomUserOAuthTests.Start.cs` / `.Complete.cs` / `.Refresh.cs` — either is fine; keep the shared `CurrentUser()` / `Availability()` helpers in the Task 4 file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/DTOs/Responses/ZoomOAuthResponses.cs \
        src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/StartZoomUserOAuth/StartZoomUserOAuthCommand.cs \
        tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthTests.cs
git commit -m "feat(zoom): add StartZoomUserOAuthCommand"
```

---

### Task 5: `CompleteZoomUserOAuthCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/CompleteZoomUserOAuth/CompleteZoomUserOAuthCommand.cs`
- Test: add to `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthTests.cs`

**Interfaces:**
- Consumes: `ZoomUserOAuthRules`, `ZoomUserIntegrationAvailability`, `IOAuthStateProtector.TryUnprotect(string, out GitHubOAuthState?) -> bool`, `IPlatformOAuthAppResolver.GetActiveCredentialForProviderAsync("zoom", ct) -> ResolvedPlatformOAuthAppCredential?` (`.ClientSecret`), `IZoomOAuthClient` (Task 2), `ISender` to send `UpsertOwnUserIntegrationConnectionCommand` (existing, generic).
- Produces:
  - `record CompleteZoomUserOAuthCommand(string? Code, string? State, string RedirectUri) : IRequest<Result<ZoomOAuthCompleteResponse>>`
  - `class CompleteZoomUserOAuthCommandHandler` — mirrors `CompleteGitHubUserOAuthCommandHandler`: null-checks code/state, `TryUnprotect`, validates state (expiry, tenant, user, `IntegrationKey == "zoom"`, `Provider == "zoom"`, returnUrl still safe, sessionBinding), availability, resolve credential, `ExchangeCodeAsync`, `GetCurrentUserAsync`, then `UpsertOwnUserIntegrationConnectionCommand("zoom", profile?.ProviderUserId, profile?.DisplayName, profile?.Email, token.AccessToken, token.RefreshToken, expiresAt, scopes)`.
  - Scopes: `ParseScopes(token.Scope, ZoomUserOAuthRules.RequiredScopes)` — split on space/comma; fall back to `RequiredScopes` when the response has no `scope`.
  - Expiry: `token.ExpiresInSeconds` → `DateTimeOffset.UtcNow.AddSeconds(value)`, else `null`.

- [ ] **Step 1: Write the failing test**

```csharp
using MediatR;
using NSubstitute;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteZoomUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed partial class ZoomUserOAuthTests
{
    private static GitHubOAuthState ValidState() => new(
        "nonce", TenantId, UserId, "zoom", "zoom", "/settings/integrations",
        DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(9), "session-1");

    [Fact]
    public async Task Complete_exchanges_code_and_upserts_connection()
    {
        var protector = Substitute.For<IOAuthStateProtector>();
        protector.TryUnprotect("good-state", out Arg.Any<GitHubOAuthState?>())
            .Returns(call => { call[1] = ValidState(); return true; });

        var apps = Substitute.For<IPlatformOAuthAppResolver>();
        apps.GetActiveCredentialForProviderAsync("zoom", Arg.Any<CancellationToken>())
            .Returns(new ResolvedPlatformOAuthAppCredential("zoom", "client-id", "secret", null, 1));

        var zoom = Substitute.For<IZoomOAuthClient>();
        zoom.ExchangeCodeAsync(Arg.Any<ZoomOAuthTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ZoomOAuthTokenResult("at", "rt", 3599, "user:read meeting:write", "bearer"));
        zoom.GetCurrentUserAsync("at", Arg.Any<CancellationToken>())
            .Returns(new ZoomUserProfileResult("zoom-user-1", "Dapi S", "dapi@example.com"));

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<UpsertOwnUserIntegrationConnectionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserIntegrationConnectionDto>.Success(
                new UserIntegrationConnectionDto("zoom", "connected", "zoom-user-1", "Dapi S", "dapi@example.com",
                    DateTimeOffset.UtcNow.AddHours(1), new[] { "user:read" }, null, null, DateTimeOffset.UtcNow, null)));

        var handler = new CompleteZoomUserOAuthCommandHandler(
            CurrentUser(), Availability(), protector, apps, zoom, sender);

        var result = await handler.Handle(
            new CompleteZoomUserOAuthCommand("the-code", "good-state", "https://t.test/cb"), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("connected", result.Value!.Status);
        await sender.Received(1).Send(
            Arg.Is<UpsertOwnUserIntegrationConnectionCommand>(c =>
                c.IntegrationKey == "zoom" && c.AccessToken == "at" && c.RefreshToken == "rt"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Complete_fails_when_state_is_for_a_different_provider()
    {
        var protector = Substitute.For<IOAuthStateProtector>();
        protector.TryUnprotect("x", out Arg.Any<GitHubOAuthState?>())
            .Returns(call =>
            {
                call[1] = new GitHubOAuthState("n", TenantId, UserId, "github", "github", null,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(9), "session-1");
                return true;
            });

        var handler = new CompleteZoomUserOAuthCommandHandler(
            CurrentUser(), Availability(), protector,
            Substitute.For<IPlatformOAuthAppResolver>(), Substitute.For<IZoomOAuthClient>(), Substitute.For<ISender>());

        var result = await handler.Handle(new CompleteZoomUserOAuthCommand("c", "x", "https://t.test/cb"), default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Complete_fails_502_when_token_exchange_returns_null()
    {
        var protector = Substitute.For<IOAuthStateProtector>();
        protector.TryUnprotect("s", out Arg.Any<GitHubOAuthState?>())
            .Returns(call => { call[1] = ValidState(); return true; });
        var apps = Substitute.For<IPlatformOAuthAppResolver>();
        apps.GetActiveCredentialForProviderAsync("zoom", Arg.Any<CancellationToken>())
            .Returns(new ResolvedPlatformOAuthAppCredential("zoom", "client-id", "secret", null, 1));
        var zoom = Substitute.For<IZoomOAuthClient>();
        zoom.ExchangeCodeAsync(Arg.Any<ZoomOAuthTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns((ZoomOAuthTokenResult?)null);

        var handler = new CompleteZoomUserOAuthCommandHandler(
            CurrentUser(), Availability(), protector, apps, zoom, Substitute.For<ISender>());

        var result = await handler.Handle(new CompleteZoomUserOAuthCommand("c", "s", "https://t.test/cb"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthTests"`
Expected: FAIL — `CompleteZoomUserOAuthCommand` missing.

- [ ] **Step 3: Write minimal implementation**

`CompleteZoomUserOAuthCommand.cs` — adapt `CompleteGitHubUserOAuthCommandHandler` verbatim, substituting: `ZoomUserOAuthRules` for `GitHubUserOAuthRules`; `ZoomUserIntegrationAvailability` for `GitHubUserIntegrationAvailability`; `IZoomOAuthClient` + `ZoomOAuthTokenRequest` for the GitHub client/request; `ZoomOAuthCompleteResponse(IntegrationKey, "connected", profile?.Email, state.ReturnUrl)` as the success value; `ParseScopes(token.Scope, ZoomUserOAuthRules.RequiredScopes.ToArray())`.

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteZoomUserOAuth;

public sealed record CompleteZoomUserOAuthCommand(string? Code, string? State, string RedirectUri)
    : IRequest<Result<ZoomOAuthCompleteResponse>>;

public sealed class CompleteZoomUserOAuthCommandHandler
    : IRequestHandler<CompleteZoomUserOAuthCommand, Result<ZoomOAuthCompleteResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ZoomUserIntegrationAvailability _availability;
    private readonly IOAuthStateProtector _stateProtector;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IZoomOAuthClient _zoom;
    private readonly ISender _sender;

    public CompleteZoomUserOAuthCommandHandler(
        ICurrentUser currentUser,
        ZoomUserIntegrationAvailability availability,
        IOAuthStateProtector stateProtector,
        IPlatformOAuthAppResolver oauthApps,
        IZoomOAuthClient zoom,
        ISender sender)
    {
        _currentUser = currentUser;
        _availability = availability;
        _stateProtector = stateProtector;
        _oauthApps = oauthApps;
        _zoom = zoom;
        _sender = sender;
    }

    public async Task<Result<ZoomOAuthCompleteResponse>> Handle(CompleteZoomUserOAuthCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result<ZoomOAuthCompleteResponse>.Failure("OAuth code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            return Result<ZoomOAuthCompleteResponse>.Failure("OAuth state is required.");
        }

        if (!_stateProtector.TryUnprotect(request.State, out var state) || state is null)
        {
            return Result<ZoomOAuthCompleteResponse>.Failure("OAuth state is invalid.");
        }

        var stateError = ValidateState(state);
        if (stateError is not null)
        {
            return Result<ZoomOAuthCompleteResponse>.Failure(stateError);
        }

        var availability = await _availability.ValidateAsync(_currentUser.TenantId, cancellationToken);
        if (!availability.IsSuccess)
        {
            return Result<ZoomOAuthCompleteResponse>.Failure(
                availability.Error ?? "Zoom integration is unavailable.", availability.StatusCode ?? 400);
        }

        var credential = await _oauthApps.GetActiveCredentialForProviderAsync(ZoomUserOAuthRules.Provider, cancellationToken);
        if (credential is null)
        {
            return Result<ZoomOAuthCompleteResponse>.Failure("Zoom OAuth credential is unavailable.", 422);
        }

        var app = availability.Value!;
        var token = await _zoom.ExchangeCodeAsync(
            new ZoomOAuthTokenRequest(app.TokenUrl, app.ClientId, credential.ClientSecret, request.Code, request.RedirectUri),
            cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return Result<ZoomOAuthCompleteResponse>.Failure("Zoom authorization failed.", 502);
        }

        var profile = await _zoom.GetCurrentUserAsync(token.AccessToken, cancellationToken);
        var scopes = ParseScopes(token.Scope, ZoomUserOAuthRules.RequiredScopes.ToArray());
        var expiresAt = token.ExpiresInSeconds.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds.Value)
            : (DateTimeOffset?)null;

        var stored = await _sender.Send(
            new UpsertOwnUserIntegrationConnectionCommand(
                ZoomUserOAuthRules.IntegrationKey,
                profile?.ProviderUserId,
                profile?.DisplayName,
                profile?.Email,
                token.AccessToken,
                token.RefreshToken,
                expiresAt,
                scopes),
            cancellationToken);
        if (!stored.IsSuccess)
        {
            return Result<ZoomOAuthCompleteResponse>.Failure(
                stored.Error ?? "Zoom connection could not be stored.", stored.StatusCode ?? 400);
        }

        return Result<ZoomOAuthCompleteResponse>.Success(
            new ZoomOAuthCompleteResponse(ZoomUserOAuthRules.IntegrationKey, "connected", profile?.Email, state.ReturnUrl));
    }

    private string? ValidateState(GitHubOAuthState state)
    {
        if (state.ExpiresAtUtc <= DateTimeOffset.UtcNow || state.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return "OAuth state has expired.";
        }

        if (state.TenantId != _currentUser.TenantId)
        {
            return "OAuth state does not match the current tenant.";
        }

        if (state.UserId != _currentUser.UserId)
        {
            return "OAuth state does not match the current user.";
        }

        if (!string.Equals(state.IntegrationKey, ZoomUserOAuthRules.IntegrationKey, StringComparison.Ordinal) ||
            !string.Equals(state.Provider, ZoomUserOAuthRules.Provider, StringComparison.Ordinal))
        {
            return "OAuth state is invalid for Zoom.";
        }

        if (ZoomUserOAuthRules.ValidateReturnUrl(state.ReturnUrl) != state.ReturnUrl)
        {
            return "OAuth return URL is invalid.";
        }

        if (!string.Equals(state.SessionBinding, _currentUser.SessionBinding, StringComparison.Ordinal))
        {
            return "OAuth state does not match the current session.";
        }

        return null;
    }

    private static string[] ParseScopes(string? providerScopes, string[] defaults)
    {
        if (string.IsNullOrWhiteSpace(providerScopes))
        {
            return defaults;
        }

        return providerScopes.Split(
            new[] { ' ', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthTests"`
Expected: PASS (6 tests total in the class now).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/CompleteZoomUserOAuth/CompleteZoomUserOAuthCommand.cs \
        tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthTests.cs
git commit -m "feat(zoom): add CompleteZoomUserOAuthCommand"
```

---

### Task 6: `GetZoomUserIntegrationStatusQuery` + `RefreshOwnZoomConnectionCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Queries/GetZoomUserIntegrationStatus/GetZoomUserIntegrationStatusQuery.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/RefreshOwnZoomConnection/RefreshOwnZoomConnectionCommand.cs`
- Test: add to `tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthTests.cs`

**Interfaces:**
- Consumes: `IUserIntegrationConnectionRepository.GetActiveAsync(tenantId, userId, "zoom", ct)`, `UserIntegrationConnectionMapper.ToSafeDto` / `.Disconnected`, `IEncryptionService.Decrypt`, `IZoomOAuthClient.RefreshTokenAsync`, `IPlatformOAuthAppResolver`, `ZoomUserIntegrationAvailability`, `ISender` → `UpsertOwnUserIntegrationConnectionCommand`.
- Produces:
  - `record GetZoomUserIntegrationStatusQuery : IRequest<Result<UserIntegrationConnectionDto>>` + handler returning the caller's `zoom` connection DTO (or `UserIntegrationConnectionMapper.Disconnected("zoom")`).
  - `record RefreshOwnZoomConnectionCommand : IRequest<Result<UserIntegrationConnectionDto>>` + handler mirroring `RefreshOwnGitHubConnectionCommandHandler`: NotFound when not connected; `422` when the stored connection has no refresh token; `502` when `RefreshTokenAsync` returns null; on success re-upsert with `replacementRefreshToken = refreshed.RefreshToken ?? oldRefreshToken` (**Zoom always returns a new one, so this normally rotates**), recomputed `expiresAt`, and `ParseScopes(refreshed.Scope, connection.ScopesGranted ?? ZoomUserOAuthRules.RequiredScopes.ToArray())`.

- [ ] **Step 1: Write the failing test**

```csharp
using MediatR;
using NSubstitute;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.RefreshOwnZoomConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetZoomUserIntegrationStatus;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed partial class ZoomUserOAuthTests
{
    [Fact]
    public async Task Status_returns_disconnected_when_no_connection()
    {
        var repo = Substitute.For<IUserIntegrationConnectionRepository>();
        repo.GetActiveAsync(TenantId, UserId, "zoom", Arg.Any<CancellationToken>()).Returns((UserIntegrationConnection?)null);

        var handler = new GetZoomUserIntegrationStatusQueryHandler(CurrentUser(), repo);
        var result = await handler.Handle(new GetZoomUserIntegrationStatusQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("zoom", result.Value!.IntegrationKey);
        Assert.Equal("disconnected", result.Value.Status);
    }

    [Fact]
    public async Task Refresh_rotates_refresh_token_and_reupserts()
    {
        var repo = Substitute.For<IUserIntegrationConnectionRepository>();
        repo.GetActiveAsync(TenantId, UserId, "zoom", Arg.Any<CancellationToken>()).Returns(new UserIntegrationConnection
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, IntegrationKey = "zoom",
            RefreshTokenEncrypted = "enc-old-rt", ProviderEmail = "d@e.test", Status = "connected",
            ScopesGranted = new[] { "meeting:write" }
        });

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("enc-old-rt").Returns("old-rt");

        var apps = Substitute.For<IPlatformOAuthAppResolver>();
        apps.GetActiveCredentialForProviderAsync("zoom", Arg.Any<CancellationToken>())
            .Returns(new ResolvedPlatformOAuthAppCredential("zoom", "client-id", "secret", null, 1));

        var zoom = Substitute.For<IZoomOAuthClient>();
        zoom.RefreshTokenAsync(Arg.Any<ZoomOAuthRefreshRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ZoomOAuthTokenResult("new-at", "new-rt", 3599, "meeting:write", "bearer"));

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<UpsertOwnUserIntegrationConnectionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserIntegrationConnectionDto>.Success(
                new UserIntegrationConnectionDto("zoom", "connected", null, null, "d@e.test",
                    DateTimeOffset.UtcNow.AddHours(1), new[] { "meeting:write" }, null, null, DateTimeOffset.UtcNow, null)));

        var handler = new RefreshOwnZoomConnectionCommandHandler(
            CurrentUser(), repo, Availability(), apps, zoom, encryption, sender);

        var result = await handler.Handle(new RefreshOwnZoomConnectionCommand(), default);

        Assert.True(result.IsSuccess, result.Error);
        await sender.Received(1).Send(
            Arg.Is<UpsertOwnUserIntegrationConnectionCommand>(c => c.RefreshToken == "new-rt" && c.AccessToken == "new-at"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_returns_not_found_when_not_connected()
    {
        var repo = Substitute.For<IUserIntegrationConnectionRepository>();
        repo.GetActiveAsync(TenantId, UserId, "zoom", Arg.Any<CancellationToken>()).Returns((UserIntegrationConnection?)null);

        var handler = new RefreshOwnZoomConnectionCommandHandler(
            CurrentUser(), repo, Availability(), Substitute.For<IPlatformOAuthAppResolver>(),
            Substitute.For<IZoomOAuthClient>(), Substitute.For<IEncryptionService>(), Substitute.For<ISender>());

        var result = await handler.Handle(new RefreshOwnZoomConnectionCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthTests"`
Expected: FAIL — `GetZoomUserIntegrationStatusQuery` / `RefreshOwnZoomConnectionCommand` missing.

- [ ] **Step 3: Write minimal implementation**

`GetZoomUserIntegrationStatusQuery.cs` — copy `GetGitHubUserIntegrationStatusQueryHandler`, swap `GitHubUserOAuthRules.IntegrationKey` → `ZoomUserOAuthRules.IntegrationKey`.

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetZoomUserIntegrationStatus;

public sealed record GetZoomUserIntegrationStatusQuery : IRequest<Result<UserIntegrationConnectionDto>>;

public sealed class GetZoomUserIntegrationStatusQueryHandler
    : IRequestHandler<GetZoomUserIntegrationStatusQuery, Result<UserIntegrationConnectionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserIntegrationConnectionRepository _repository;

    public GetZoomUserIntegrationStatusQueryHandler(ICurrentUser currentUser, IUserIntegrationConnectionRepository repository)
    {
        _currentUser = currentUser;
        _repository = repository;
    }

    public async Task<Result<UserIntegrationConnectionDto>> Handle(GetZoomUserIntegrationStatusQuery request, CancellationToken cancellationToken)
    {
        var connection = await _repository.GetActiveAsync(
            _currentUser.TenantId, _currentUser.UserId, ZoomUserOAuthRules.IntegrationKey, cancellationToken);
        return connection is null
            ? Result<UserIntegrationConnectionDto>.Success(UserIntegrationConnectionMapper.Disconnected(ZoomUserOAuthRules.IntegrationKey))
            : Result<UserIntegrationConnectionDto>.Success(UserIntegrationConnectionMapper.ToSafeDto(connection));
    }
}
```

`RefreshOwnZoomConnectionCommand.cs` — copy `RefreshOwnGitHubConnectionCommandHandler`, swapping the same names, `IZoomOAuthClient` / `ZoomOAuthRefreshRequest`, and the scope fallback to `ZoomUserOAuthRules.RequiredScopes.ToArray()`. Constructor parameter order must match the test: `(ICurrentUser, IUserIntegrationConnectionRepository, ZoomUserIntegrationAvailability, IPlatformOAuthAppResolver, IZoomOAuthClient, IEncryptionService, ISender)`.

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.RefreshOwnZoomConnection;

public sealed record RefreshOwnZoomConnectionCommand : IRequest<Result<UserIntegrationConnectionDto>>;

public sealed class RefreshOwnZoomConnectionCommandHandler
    : IRequestHandler<RefreshOwnZoomConnectionCommand, Result<UserIntegrationConnectionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserIntegrationConnectionRepository _repository;
    private readonly ZoomUserIntegrationAvailability _availability;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IZoomOAuthClient _zoom;
    private readonly IEncryptionService _encryption;
    private readonly ISender _sender;

    public RefreshOwnZoomConnectionCommandHandler(
        ICurrentUser currentUser,
        IUserIntegrationConnectionRepository repository,
        ZoomUserIntegrationAvailability availability,
        IPlatformOAuthAppResolver oauthApps,
        IZoomOAuthClient zoom,
        IEncryptionService encryption,
        ISender sender)
    {
        _currentUser = currentUser;
        _repository = repository;
        _availability = availability;
        _oauthApps = oauthApps;
        _zoom = zoom;
        _encryption = encryption;
        _sender = sender;
    }

    public async Task<Result<UserIntegrationConnectionDto>> Handle(RefreshOwnZoomConnectionCommand request, CancellationToken cancellationToken)
    {
        var available = await _availability.ValidateAsync(_currentUser.TenantId, cancellationToken);
        if (!available.IsSuccess)
        {
            return Result<UserIntegrationConnectionDto>.Failure(
                available.Error ?? "Zoom integration is unavailable.", available.StatusCode ?? 400);
        }

        var connection = await _repository.GetActiveAsync(
            _currentUser.TenantId, _currentUser.UserId, ZoomUserOAuthRules.IntegrationKey, cancellationToken);
        if (connection is null)
        {
            return Result<UserIntegrationConnectionDto>.NotFound("Zoom is not connected for the current user.");
        }

        if (string.IsNullOrWhiteSpace(connection.RefreshTokenEncrypted))
        {
            return Result<UserIntegrationConnectionDto>.Failure(
                "This Zoom connection does not support token refresh. Reconnect the account.", 422);
        }

        var credential = await _oauthApps.GetActiveCredentialForProviderAsync(ZoomUserOAuthRules.Provider, cancellationToken);
        if (credential is null)
        {
            return Result<UserIntegrationConnectionDto>.Failure("Zoom OAuth credential is unavailable.", 422);
        }

        var refreshToken = _encryption.Decrypt(connection.RefreshTokenEncrypted);
        var app = available.Value!;
        var refreshed = await _zoom.RefreshTokenAsync(
            new ZoomOAuthRefreshRequest(app.TokenUrl, app.ClientId, credential.ClientSecret, refreshToken),
            cancellationToken);
        if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
        {
            return Result<UserIntegrationConnectionDto>.Failure("Zoom token refresh failed.", 502);
        }

        var replacementRefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
            ? refreshToken
            : refreshed.RefreshToken;
        var scopes = ParseScopes(refreshed.Scope, connection.ScopesGranted ?? ZoomUserOAuthRules.RequiredScopes.ToArray());
        var expiresAt = refreshed.ExpiresInSeconds.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresInSeconds.Value)
            : connection.TokenExpiresAt;

        return await _sender.Send(
            new UpsertOwnUserIntegrationConnectionCommand(
                ZoomUserOAuthRules.IntegrationKey,
                connection.ProviderUserId,
                connection.ProviderUsername,
                connection.ProviderEmail,
                refreshed.AccessToken,
                replacementRefreshToken,
                expiresAt,
                scopes),
            cancellationToken);
    }

    private static string[] ParseScopes(string? providerScopes, string[] existingScopes)
    {
        if (string.IsNullOrWhiteSpace(providerScopes))
        {
            return existingScopes;
        }

        return providerScopes.Split(
            new[] { ' ', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ZoomUserOAuthTests"`
Expected: PASS (9 tests total).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Queries/GetZoomUserIntegrationStatus/ \
        src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/Commands/RefreshOwnZoomConnection/ \
        tests/ONEVO.Tests.Unit/Features/SharedPlatform/TenantIntegrations/ZoomUserOAuthTests.cs
git commit -m "feat(zoom): add Zoom connection status query and token refresh command"
```

---

### Task 7: `ZoomIntegrationController`

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Integrations/ZoomIntegrationController.cs`
- Create: `src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/DTOs/Requests/StartZoomOAuthRequest.cs`
- Test: `tests/ONEVO.Tests.Architecture/` — add `ZoomIntegrationController` to whatever existing convention test enumerates tenant controllers (search for `GitHubIntegrationController` in `tests/ONEVO.Tests.Architecture`; if a "every controller has `[Authorize]`" or route-prefix test exists it will pick the new controller up automatically — just run the suite). No new arch test is required; the deliverable is a compiling, correctly-routed controller.

**Interfaces:**
- Consumes: `IMediator`; `StartZoomUserOAuthCommand`, `CompleteZoomUserOAuthCommand`, `GetZoomUserIntegrationStatusQuery`, `RefreshOwnZoomConnectionCommand` (Tasks 4-6); `DisconnectOwnUserIntegrationCommand("zoom")` (existing, generic); `ZoomUserOAuthRules.IntegrationKey`.
- Produces: `ZoomIntegrationController` with routes under `api/v1/integrations/zoom`.

- [ ] **Step 1: Write the controller (no unit test — covered by arch suite + Plan-level integration)**

`StartZoomOAuthRequest.cs`:

```csharp
namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Requests;

public sealed record StartZoomOAuthRequest(string? ReturnUrl);
```

`ZoomIntegrationController.cs` — mirror `GitHubIntegrationController` but without the `tenant/enable` / `tenant/disable` / `tenant/status` actions (Zoom has no tenant-approval step):

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteZoomUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectOwnUserIntegration;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.RefreshOwnZoomConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartZoomUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Requests;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetZoomUserIntegrationStatus;

namespace ONEVO.Api.Controllers.Tenant.Integrations;

[ApiController]
[Route("api/v1/integrations/zoom")]
[Authorize(Policy = "TenantPolicy")]
public sealed class ZoomIntegrationController : ControllerBase
{
    private readonly IMediator _mediator;

    public ZoomIntegrationController(IMediator mediator) => _mediator = mediator;

    [HttpPost("connect/start")]
    public async Task<IActionResult> Start([FromBody] StartZoomOAuthRequest request, CancellationToken ct)
    {
        var redirectUri = Url.ActionLink(nameof(Callback), values: null, protocol: Request.Scheme);
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem("Zoom callback URL could not be generated.", statusCode: 500);
        }

        var result = await _mediator.Send(new StartZoomUserOAuthCommand(request.ReturnUrl, redirectUri), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("connect/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        var redirectUri = Url.ActionLink(nameof(Callback), values: null, protocol: Request.Scheme);
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem("Zoom callback URL could not be generated.", statusCode: 500);
        }

        var result = await _mediator.Send(new CompleteZoomUserOAuthCommand(code, state, redirectUri), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetZoomUserIntegrationStatusQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var result = await _mediator.Send(new DisconnectOwnUserIntegrationCommand(ZoomUserOAuthRules.IntegrationKey), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshOwnZoomConnectionCommand(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 2: Build and run the architecture + unit suites**

Run: `dotnet build ONEVO.sln` then
`dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj` and
`dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TenantIntegrations"`
Expected: build succeeds; both suites PASS (arch suite auto-includes the new controller in any "controllers are authorized / correctly prefixed" convention test).

- [ ] **Step 3: Manual route check**

Run: `dotnet run --project src/ONEVO.Api -- --urls http://localhost:5xxx` is **not** required. Instead, confirm the five routes compile into the route table by grepping the generated Swagger doc if the project emits one, or simply trust the arch suite. Skip if no Swagger.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Integrations/ZoomIntegrationController.cs \
        src/ONEVO.Application/Features/SharedPlatform/TenantIntegrations/DTOs/Requests/StartZoomOAuthRequest.cs
git commit -m "feat(zoom): add ZoomIntegrationController (connect/callback/status/disconnect/refresh)"
```

---

### Task 8: Dev seeder — `zoom` integration catalog entry

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`

**Interfaces:**
- Consumes: the existing `SeedGitHubIntegrationCatalogAsync(db, platformUserId, now, ct)` private method and its call site (~line 264) in the same file; `IntegrationCatalogEntry` entity; `db.IntegrationCatalogEntries`.
- Produces: a `SeedZoomIntegrationCatalogAsync(db, platformUserId, now, ct)` private method, idempotent (skips if an entry with `IntegrationKey == "zoom"` already exists), inserting `{ IntegrationKey = "zoom", DisplayName = "Zoom", ConnectionScope = "user", OnevoAppProvider = "zoom", IsActive = true, CreatedById = platformUserId, CreatedAt = now }`, plus a `ModuleIntegrationLink` to the `calendar` module if the GitHub helper also links a module (match its shape). Called right after the `SeedGitHubIntegrationCatalogAsync(...)` call.

- [ ] **Step 1: Read the existing GitHub seed method**

Run: `sed -n '900,960p' src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`
Note the exact entity shape, the module-link shape (if any), and the idempotency check it uses.

- [ ] **Step 2: Add `SeedZoomIntegrationCatalogAsync` and its call**

Add the call beside the GitHub one (~line 264):

```csharp
            await SeedZoomIntegrationCatalogAsync(db, platformUser.Id, now, ct);
```

Add the method next to `SeedGitHubIntegrationCatalogAsync`, copying its structure exactly and substituting the `zoom` values above. If the GitHub method creates a `ModuleIntegrationLink`, create the equivalent link from `"zoom"` to the same module key the GitHub method links (or to `"calendar"` if GitHub links per-integration relevant modules — match intent).

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: build succeeds.

- [ ] **Step 4: Run the integration suite's seeder path if one exists**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~Seeder" `
Expected: PASS, or "no matching tests" (acceptable — the seeder runs at app start; a green `dotnet build` plus the idempotency guard is sufficient here).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs
git commit -m "feat(zoom): seed the zoom integration_catalog entry for dev/test environments"
```

---

### Task 9: Full-suite green + plan wrap-up

**Files:** none (verification only).

- [ ] **Step 1: Run the whole unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS — no regressions in existing GitHub / calendar / integration tests.

- [ ] **Step 2: Run the architecture suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj`
Expected: PASS.

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build ONEVO.sln -c Release`
Expected: 0 errors.

- [ ] **Step 4: Commit any final fixups, then push the branch**

```bash
git status   # expect clean
git push -u origin feature/zoom-calendar-integration
```

---

## Self-Review

**1. Spec coverage (spec §"Architecture" item 1 — "Per-user Zoom connection"):**
- Controller `api/v1/integrations/zoom` with start/callback/status/disconnect/refresh → Tasks 4-7. ✅
- Tokens in `UserIntegrationConnection` (`integrationKey = "zoom"`), encrypted via the same service as GitHub → reuse of `UpsertOwnUserIntegrationConnectionCommand` in Tasks 5-6. ✅
- OAuth `state` via `IOAuthStateProtector`; return-URL validation → Task 1 (`OAuthReturnUrlRules`), Tasks 4-5. ✅
- `IZoomOAuthClient` (typed HttpClient), Basic-auth token endpoint, rotating refresh token → Task 2 + Task 6. ✅
- Client id/secret from `platform_oauth_apps` (provider `zoom`) → `IPlatformOAuthAppResolver` use in Tasks 5-6; operator-configured (prerequisite section). ✅
- `integration_catalog` seed row → Task 8 (dev/test) + operator prerequisite (prod). ✅
- Scopes `user:read meeting:read meeting:write` requested at authorize, catalog default not widened → Task 1 `RequiredScopes` + Global Constraints. ✅
- **Deferred from this plan (spec §"Architecture" item 4 — proactive `ZoomTokenRefreshJob`):** not built here. Lazy refresh (per-call, in `ZoomConferencingProvider`) lands in Plan 2; the manual `POST refresh` endpoint (Task 6) covers the interim. The cross-tenant `BackgroundService` refresh job is Plan 3 / Phase 2. This is an intentional scope cut recorded here.
- **Not in this plan (later plans):** `IConferencingProvider` / `ZoomConferencingProvider` / `IZoomMeetingsClient` / RRULE mapper → Plan 2. Migration, DTO, outbox handlers, calendar handler changes, retry endpoint, invite-email link → Plan 3. Frontend → separate frontend plan.

**2. Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N"/uncoded steps. Task 7 Step 3 and Task 8 Step 1 direct the engineer to read an existing file first — those are concrete `sed` commands, not placeholders. Task 7 has no unit test by design (thin controller); its verification is the build + arch suite, stated explicitly.

**3. Type consistency:**
- `ZoomUserOAuthRules.IntegrationKey` / `.Provider` / `.RequiredScopes` / `.BuildAuthorizationUrl` / `.ValidateReturnUrl` — defined Task 1, used identically Tasks 4-8. ✅
- `IZoomOAuthClient` methods `ExchangeCodeAsync` / `RefreshTokenAsync` / `GetCurrentUserAsync` and records `ZoomOAuthTokenRequest` / `ZoomOAuthRefreshRequest` / `ZoomOAuthTokenResult` / `ZoomUserProfileResult` — defined Task 2, consumed Tasks 5-6. ✅
- `ZoomUserIntegrationAvailability.ValidateAsync(Guid, CancellationToken) -> Result<ResolvedPlatformOAuthApp>` — defined Task 3, consumed Tasks 4-6. ✅
- `ZoomOAuthStartResponse` / `ZoomOAuthCompleteResponse` — defined Task 4, used Tasks 4-5 + controller. ✅
- `RefreshOwnZoomConnectionCommandHandler` ctor arg order `(ICurrentUser, IUserIntegrationConnectionRepository, ZoomUserIntegrationAvailability, IPlatformOAuthAppResolver, IZoomOAuthClient, IEncryptionService, ISender)` — matches Task 6 test and impl. ✅
- Reused existing types verified against source: `UpsertOwnUserIntegrationConnectionCommand(string, string?, string?, string?, string?, string?, DateTimeOffset?, string[])`, `DisconnectOwnUserIntegrationCommand(string)`, `IUserIntegrationConnectionRepository.GetActiveAsync`, `UserIntegrationConnectionMapper.ToSafeDto` / `.Disconnected`, `IOAuthStateProtector.Protect` / `.TryUnprotect`, `GitHubOAuthState(string, Guid, Guid, string, string, string?, DateTimeOffset, DateTimeOffset, string?)`, `IPlatformOAuthAppResolver` + `ResolvedPlatformOAuthApp` / `ResolvedPlatformOAuthAppCredential`, `IEncryptionService.Encrypt` / `.Decrypt`. ✅

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-09-10-zoom-integration-connection-backend.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?** (Or: I can write Plan 2 — conferencing provider + Zoom Meetings client + RRULE mapper — and Plan 3 — calendar lifecycle wiring — first, so all three exist before any code is written.)
