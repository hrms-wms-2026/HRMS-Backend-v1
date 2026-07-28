using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Full-stack proof of the credential-first base-domain login flow through real HTTP requests,
/// real middleware (HostTenantResolutionMiddleware, AuthRateLimitingMiddleware), and a real
/// PostgreSQL database (including the auth_lookup_base_login_candidates function and RLS).
/// Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BaseDomainLoginIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_base_login_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private BaseDomainLoginTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // Roles + migrations must exist before the environment scope's onevo_app connection string
        // is opened by Program.cs's pre-Build() DatabaseConnectionStartupValidator, and the
        // environment scope must be in place before BaseDomainLoginTestFactory is constructed -
        // accessing _factory.Services/CreateClient() is what first triggers Program.cs's top-level
        // startup code, including hosted services like PermissionSeeder that assume migrations have
        // already applied.
        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new BaseDomainLoginTestFactory(connectionString);

        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task WrongPassword_ReturnsGeneric401()
    {
        var user = await SeedActiveUserAsync("wrong-pw-tenant", "wrongpw@test.onevo.dev", "CorrectPass1!");

        var response = await PostLoginAsync(user.Email, "IncorrectPass1!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingIssuesSessionAndCsrfCookies()
    {
        var user = await SeedActiveUserAsync("one-match-tenant", "onematch@test.onevo.dev", "CorrectPass1!");

        var response = await PostLoginAsync(user.Email, "CorrectPass1!");

        // Bootstrap dev tenants seed a required, published terms/privacy_notice version, so a
        // brand-new user without acceptance records must see legal_acceptance_required, not an
        // immediate session - proving legal blocking is now wired into the base-domain path.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"legal_acceptance_required\":true");
        body.Should().Contain("\"continue_url\":\"https://localhost/api/v1/legal/acceptances/complete-login\"");
        body.Should().NotContain("\"tenant_id\"");
        body.Should().NotContain("\"user_id\"");

        var finalResponse = await CompleteLegalAcceptanceAsync(response);

        finalResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await finalResponse.Content.ReadAsStringAsync());
        var setCookies = finalResponse.Headers.TryGetValues("Set-Cookie", out var cookieValues)
            ? cookieValues.ToList()
            : new List<string>();
        setCookies.Should().Contain(c => c.StartsWith("onevo_session=", StringComparison.Ordinal));
        setCookies.Should().Contain(c => c.StartsWith("onevo_csrf=", StringComparison.Ordinal));
        var finalBody = await finalResponse.Content.ReadAsStringAsync();
        finalBody.Should().NotContain("\"tenant_id\"");
        finalBody.Should().NotContain("\"user_id\"");
    }

    [Fact]
    public async Task LegalAcceptanceRace_AllowsOneWinner_AndInsertsOneRecordPerDocument()
    {
        var user = await SeedActiveUserAsync(
            "legal-race-tenant",
            "legal-race@test.onevo.dev",
            "CorrectPass1!");
        var login = await PostLoginAsync(user.Email, "CorrectPass1!");

        var race = Enumerable.Range(0, 8)
            .Select(_ => CompleteLegalAcceptanceAsync(login))
            .ToArray();
        var responses = await Task.WhenAll(race);

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(
            1,
            string.Join(
                Environment.NewLine,
                await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(7);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var records = await db.LegalAcceptanceRecords
            .Where(r => r.TenantId == user.TenantId && r.UserId == user.UserId)
            .ToListAsync();
        records.Should().HaveCount(2);
        records.Select(r => r.DocumentType).Should().BeEquivalentTo("terms", "privacy_notice");
    }

    [Fact]
    public async Task BasePasswordLogin_MfaThenLegal_CompletesOnRootHost()
    {
        var user = await SeedActiveUserAsync(
            "mfa-continue-tenant",
            "mfa-continue@test.onevo.dev",
            "CorrectPass1!");
        await SeedVerifiedMfaAsync(user.TenantId, user.UserId);

        var login = await PostLoginAsync(user.Email, "CorrectPass1!");
        login.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginBody = await login.Content.ReadAsStringAsync();
        using var loginDocument = JsonDocument.Parse(loginBody);
        var mfaContinue = new Uri(
            loginDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);
        mfaContinue.Host.Should().Be("localhost");
        loginBody.Should().NotContain("\"tenant_id\"");
        loginBody.Should().NotContain("\"user_id\"");

        var mfaCookie = ExtractCookieValue(login, "onevo_mfa");
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, mfaContinue.PathAndQuery);
        verifyRequest.Headers.Host = mfaContinue.Host;
        verifyRequest.Headers.Add("Cookie", $"onevo_mfa={mfaCookie}");
        verifyRequest.Content = JsonContent.Create(new { code = "123456" });
        var verified = await _client.SendAsync(verifyRequest);

        verified.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await verified.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"legal_acceptance_required\":true");

        var completed = await CompleteLegalAcceptanceAsync(verified);
        completed.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await completed.Content.ReadAsStringAsync());
        var completedBody = await completed.Content.ReadAsStringAsync();
        completedBody.Should().NotContain("\"tenant_id\"");
        completedBody.Should().NotContain("\"user_id\"");
    }

    [Fact]
    public async Task BaseGoogleLogin_MfaThenLegal_CompletesOnRootHost()
    {
        var user = await SeedActiveUserAsync(
            "google-mfa-tenant",
            "google-mfa@test.onevo.dev",
            "UnusedPassword1!");
        await SeedVerifiedMfaAsync(user.TenantId, user.UserId);

        using var googleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login/google");
        googleRequest.Headers.Host = "localhost";
        googleRequest.Content = JsonContent.Create(new { google_id_token = user.Email });
        var login = await _client.SendAsync(googleRequest);

        login.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginBody = await login.Content.ReadAsStringAsync();
        using var loginDocument = JsonDocument.Parse(loginBody);
        var mfaContinue = new Uri(
            loginDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);
        mfaContinue.Host.Should().Be("localhost");
        loginBody.Should().NotContain("\"tenant_id\"");
        loginBody.Should().NotContain("\"user_id\"");

        var mfaCookie = ExtractCookieValue(login, "onevo_mfa");
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, mfaContinue.PathAndQuery);
        verifyRequest.Headers.Host = mfaContinue.Host;
        verifyRequest.Headers.Add("Cookie", $"onevo_mfa={mfaCookie}");
        verifyRequest.Content = JsonContent.Create(new { code = "123456" });
        var verified = await _client.SendAsync(verifyRequest);

        verified.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var completed = await CompleteLegalAcceptanceAsync(verified);
        completed.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await completed.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.LegalLoginChallenges
            .Where(c => c.TenantId == user.TenantId && c.UserId == user.UserId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.Origin)
            .FirstAsync())
            .Should()
            .Be("google_sso");
    }

    [Fact]
    public async Task TenantHostPasswordLogin_IsRejected_AndCreatesNoSession()
    {
        var user = await SeedActiveUserAsync(
            "tenant-reject-host",
            "tenant-reject-host@test.onevo.dev",
            "CorrectPass1!");

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "tenant-reject-host.localhost";
        loginRequest.Content = JsonContent.Create(new
        {
            email = user.Email,
            password = "CorrectPass1!"
        });
        var response = await _client.SendAsync(loginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant-host password login is not supported.");
        body.Should().NotContain("main login page");

        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse(
            "a rejected tenant-host password login must not set onevo_session, onevo_csrf, or onevo_mfa");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasSession = await db.Sessions.AnyAsync(s => s.UserId == user.UserId);
        hasSession.Should().BeFalse("no session row may be created by a rejected tenant-host password login");
    }

    [Fact]
    public async Task MultipleMatches_Returns202_WithSafeWorkspaceListOnly()
    {
        const string sharedEmail = "multimatch@test.onevo.dev";
        const string sharedPassword = "SharedPass1!";
        var tenantA = await SeedActiveUserAsync("multi-a", sharedEmail, sharedPassword);
        var tenantB = await SeedActiveUserAsync("multi-b", sharedEmail, sharedPassword);

        var response = await PostLoginAsync(sharedEmail, sharedPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"requires_workspace_selection\":true");
        body.Should().Contain("multi-a");
        body.Should().Contain("multi-b");
        body.Should().NotContain(tenantA.TenantId.ToString());
        body.Should().NotContain(tenantB.TenantId.ToString());
        body.Should().NotContain(tenantA.UserId.ToString());
        body.Should().NotContain("password_hash");
    }

    [Fact]
    public async Task WorkspaceSelection_CompletesLogin_AndSetsSessionCookie()
    {
        const string sharedEmail = "selectcompletes@test.onevo.dev";
        const string sharedPassword = "SharedPass1!";
        var tenantA = await SeedActiveUserAsync("select-a", sharedEmail, sharedPassword);
        await SeedActiveUserAsync("select-b", sharedEmail, sharedPassword);

        var loginResponse = await PostLoginAsync(sharedEmail, sharedPassword);
        var loginChallenge = await ExtractLoginChallengeAsync(loginResponse);

        var selectionResponse = await PostSelectWorkspaceAsync(loginChallenge, "select-a");

        selectionResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var selectionBody = await selectionResponse.Content.ReadAsStringAsync();
        selectionBody.Should().Contain("\"legal_acceptance_required\":true");

        var finalResponse = await CompleteLegalAcceptanceAsync(selectionResponse);

        finalResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await finalResponse.Content.ReadAsStringAsync());
        var setCookies = finalResponse.Headers.TryGetValues("Set-Cookie", out var cookieValues)
            ? cookieValues.ToList()
            : new List<string>();
        setCookies.Should().Contain(c => c.StartsWith("onevo_session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkspaceSelectionRace_AllowsExactlyOneSessionWinner()
    {
        const string sharedEmail = "racewinner@test.onevo.dev";
        const string sharedPassword = "SharedPass1!";
        await SeedActiveUserAsync("race-a", sharedEmail, sharedPassword);
        await SeedActiveUserAsync("race-b", sharedEmail, sharedPassword);

        var loginResponse = await PostLoginAsync(sharedEmail, sharedPassword);
        var loginChallenge = await ExtractLoginChallengeAsync(loginResponse);

        var raceTasks = Enumerable.Range(0, 8)
            .Select(_ => PostSelectWorkspaceAsync(loginChallenge, "race-a"))
            .ToArray();
        var responses = await Task.WhenAll(raceTasks);

        // A brand-new user hits legal_acceptance_required (202), not an immediate session (200),
        // so "won the race to consume the challenge" is now Accepted, not OK.
        var winners = responses.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        winners.Should().Be(1, "concurrent valid selections against the same challenge must allow exactly one winner");
    }

    [Fact]
    public async Task NineEligibleCandidates_ReturnsGeneric401_NotWorkspaceSelection()
    {
        const string sharedEmail = "overflow@test.onevo.dev";
        const string sharedPassword = "SharedPass1!";
        for (var i = 0; i < 9; i++)
        {
            await SeedActiveUserAsync($"overflow-{i}", sharedEmail, sharedPassword);
        }

        var response = await PostLoginAsync(sharedEmail, sharedPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("requires_workspace_selection");
    }

    [Fact]
    public async Task MixedCaseEmail_LogsInWithLowercaseCredential()
    {
        await SeedActiveUserAsync("mixed-case-tenant", "Owner@Acme.Test", "CorrectPass1!");

        var response = await PostLoginAsync("owner@acme.test", "CorrectPass1!");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MixedCaseEmail_LogsInWithUppercaseCredential()
    {
        await SeedActiveUserAsync("mixed-case-tenant-2", "Owner@Acme.Test", "CorrectPass1!");

        var response = await PostLoginAsync("OWNER@ACME.TEST", "CorrectPass1!");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DuplicateNormalizedEmail_SameTenant_IsRejectedByUniqueIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "dup-email-tenant",
            Slug = "dup-email-tenant",
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = "Owner@Acme.Test",
            PasswordHash = "irrelevant-hash",
            FirstName = "First",
            LastName = "Owner",
            IsActive = true
        });
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = "owner@acme.test",
            PasswordHash = "irrelevant-hash-2",
            FirstName = "Second",
            LastName = "Owner",
            IsActive = true
        });

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "ix_users_tenant_id_normalized_email must reject a second user whose normalized email collides within the same tenant");
    }

    [Fact]
    public async Task SameNormalizedEmail_DifferentTenants_IsAllowed()
    {
        var tenantA = await SeedActiveUserAsync("cross-tenant-a", "Shared@Acme.Test", "CorrectPass1!");
        var tenantB = await SeedActiveUserAsync("cross-tenant-b", "shared@acme.test", "CorrectPass1!");

        tenantA.TenantId.Should().NotBe(tenantB.TenantId);
    }

    [Fact]
    public async Task Cleanup_DeletesOldExpiredAndConsumedRows_ButKeepsRecentOnes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var oldExpired = new LoginWorkspaceSelectionChallenge
        {
            Id = Guid.NewGuid(),
            ChallengeHash = new string('a', 64),
            NormalizedEmail = "old-expired@test.onevo.dev",
            CandidateWorkspacesJson = "[]",
            Purpose = "workspace_selection",
            ExpiresAt = clock.UtcNow.AddHours(-30),
            CreatedAt = clock.UtcNow.AddHours(-31)
        };
        var oldConsumed = new LoginWorkspaceSelectionChallenge
        {
            Id = Guid.NewGuid(),
            ChallengeHash = new string('b', 64),
            NormalizedEmail = "old-consumed@test.onevo.dev",
            CandidateWorkspacesJson = "[]",
            Purpose = "workspace_selection",
            ExpiresAt = clock.UtcNow.AddHours(-40),
            ConsumedAt = clock.UtcNow.AddHours(-25),
            CreatedAt = clock.UtcNow.AddHours(-41)
        };
        var recentExpired = new LoginWorkspaceSelectionChallenge
        {
            Id = Guid.NewGuid(),
            ChallengeHash = new string('c', 64),
            NormalizedEmail = "recent-expired@test.onevo.dev",
            CandidateWorkspacesJson = "[]",
            Purpose = "workspace_selection",
            ExpiresAt = clock.UtcNow.AddMinutes(-10),
            CreatedAt = clock.UtcNow.AddMinutes(-20)
        };
        db.LoginWorkspaceSelectionChallenges.AddRange(oldExpired, oldConsumed, recentExpired);
        await db.SaveChangesAsync();

        var runner = scope.ServiceProvider.GetRequiredService<ILoginWorkspaceSelectionChallengeCleanupRunner>();
        var deletedCount = await runner.RunOnceAsync();

        deletedCount.Should().Be(2);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var remainingIds = await verifyDb.LoginWorkspaceSelectionChallenges.Select(c => c.Id).ToListAsync();
        remainingIds.Should().NotContain(oldExpired.Id);
        remainingIds.Should().NotContain(oldConsumed.Id);
        remainingIds.Should().Contain(recentExpired.Id);
    }

    private async Task<(Guid TenantId, Guid UserId, string Email)> SeedActiveUserAsync(
        string tenantSlug, string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = tenantSlug,
            Slug = tenantSlug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (tenant.Id, user.Id, user.Email);
    }

    private async Task SeedVerifiedMfaAsync(Guid tenantId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        db.UserMfas.Add(new UserMfa
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            MethodType = "totp",
            Secret = encryption.Encrypt("JBSWY3DPEHPK3PXP"),
            IsVerified = true
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Headers.Host = "localhost";
        request.Content = JsonContent.Create(new { email, password });
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostSelectWorkspaceAsync(string loginChallenge, string workspace)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login/select-workspace");
        request.Headers.Host = "localhost";
        request.Content = JsonContent.Create(new { login_challenge = loginChallenge, workspace });
        return await _client.SendAsync(request);
    }

    private static async Task<string> ExtractLoginChallengeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("login_challenge").GetString()!;
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : Enumerable.Empty<string>();

        foreach (var cookie in setCookies)
        {
            var pair = cookie.Split(';')[0];
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == cookieName)
                return parts[1];
        }

        throw new InvalidOperationException($"Cookie '{cookieName}' not found in response.");
    }

    /// <summary>
    /// Completes the pre-session Legal &amp; Privacy flow using the onevo_legal_pending/
    /// onevo_legal_csrf cookies from a legal_acceptance_required response, accepting the bootstrap
    /// dev terms/privacy_notice versions.
    /// </summary>
    private async Task<HttpResponseMessage> CompleteLegalAcceptanceAsync(HttpResponseMessage priorResponse)
    {
        var legalPending = ExtractCookieValue(priorResponse, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(priorResponse, "onevo_legal_csrf");
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(
            priorDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"onevo_legal_pending={legalPending}; onevo_legal_csrf={legalCsrf}");
        request.Headers.Add("X-CSRF-Token", legalCsrf);
        request.Content = JsonContent.Create(new
        {
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        });

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task LegalAcceptance_CsrfTokenInBody_IsIgnored_HeaderIsTheOnlySource()
    {
        // Proves there is no fallback to a body-supplied csrf_token: the correct token is placed
        // only in the JSON body (never in X-CSRF-Token), so this must be rejected exactly like a
        // request with no CSRF token at all.
        var user = await SeedActiveUserAsync(
            "legal-body-fallback-tenant", "legalbodyfallback@test.onevo.dev", "CorrectPass1!");
        var login = await PostLoginAsync(user.Email, "CorrectPass1!");
        var legalPending = ExtractCookieValue(login, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(login, "onevo_legal_csrf");
        var priorBody = await login.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(
            priorDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"onevo_legal_pending={legalPending}; onevo_legal_csrf={legalCsrf}");
        // Deliberately no X-CSRF-Token header - the (correct) token is only in the body, where the
        // deserializer for AcceptPendingLegalDocumentsRequest has no property to bind it to.
        request.Content = JsonContent.Create(new
        {
            csrf_token = legalCsrf,
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegalAcceptance_SuccessResponse_NeverLeaksPendingChallengeOrCsrfValue()
    {
        var user = await SeedActiveUserAsync(
            "legal-no-leak-tenant", "legalnoleak@test.onevo.dev", "CorrectPass1!");
        var login = await PostLoginAsync(user.Email, "CorrectPass1!");
        var legalPending = ExtractCookieValue(login, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(login, "onevo_legal_csrf");

        var completed = await CompleteLegalAcceptanceAsync(login);

        completed.StatusCode.Should().Be(
            HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var completedBody = await completed.Content.ReadAsStringAsync();
        completedBody.Should().NotContain(legalPending);
        completedBody.Should().NotContain(legalCsrf);
        completedBody.Should().NotContain("onevo_legal_pending");
    }

    [Fact]
    public async Task LegalAcceptance_MissingCsrfHeader_IsRejected()
    {
        var user = await SeedActiveUserAsync(
            "legal-missing-csrf-tenant", "legalmissingcsrf@test.onevo.dev", "CorrectPass1!");
        var login = await PostLoginAsync(user.Email, "CorrectPass1!");
        var legalPending = ExtractCookieValue(login, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(login, "onevo_legal_csrf");
        var priorBody = await login.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(
            priorDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"onevo_legal_pending={legalPending}; onevo_legal_csrf={legalCsrf}");
        request.Content = JsonContent.Create(new
        {
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegalAcceptance_ValidCsrfHeader_Succeeds()
    {
        var user = await SeedActiveUserAsync(
            "legal-valid-csrf-tenant", "legalvalidcsrf@test.onevo.dev", "CorrectPass1!");
        var login = await PostLoginAsync(user.Email, "CorrectPass1!");

        var completed = await CompleteLegalAcceptanceAsync(login);

        completed.StatusCode.Should().Be(
            HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
    }
}
