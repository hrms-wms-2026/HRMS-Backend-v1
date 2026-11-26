using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.Security;

/// <summary>
/// DEC-113 tenant security audit on SQL Server: a tenant admin suspends a user through the real
/// API (real login, session exchange, AuditingBehavior, RLS on) and exactly one audit_logs row is
/// written with the actor, the action and the target. A refused suspend writes no row. Also proves
/// that a nested transaction that throws inside the audit transaction rolls its own writes back.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TenantSecurityAuditIntegrationTests : IClassFixture<TenantSecurityAuditIntegrationTests.Fixture>
{
    private readonly Fixture _f;

    public TenantSecurityAuditIntegrationTests(Fixture fixture) => _f = fixture;

    [Fact]
    public async Task Suspend_by_tenant_admin_writes_exactly_one_audit_row()
    {
        var target = await _f.SeedUserAsync("suspend-ok@audit.test");

        var response = await _f.SendAsync(HttpMethod.Post, $"/api/v1/users/{target}/suspend");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var rows = await _f.AuditRowsForAsync(target);
        var row = rows.Should().ContainSingle().Subject;
        row.TenantId.Should().Be(_f.Owner.TenantId);
        row.UserId.Should().Be(_f.Owner.UserId);
        row.Action.Should().Be("user.suspended");
        row.ResourceType.Should().Be("user");
        row.ResourceId.Should().Be(target);
        row.CorrelationId.Should().NotBeNull();

        // A second suspend is refused (already suspended): still exactly one row.
        var again = await _f.SendAsync(HttpMethod.Post, $"/api/v1/users/{target}/suspend");
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _f.AuditRowsForAsync(target)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Refused_suspend_writes_no_audit_row()
    {
        // You cannot suspend your own account: the command fails with 409 and nothing is audited.
        var response = await _f.SendAsync(HttpMethod.Post, $"/api/v1/users/{_f.Owner.UserId}/suspend");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _f.AuditRowsForAsync(_f.Owner.UserId)).Should().BeEmpty();

        var unknown = Guid.NewGuid();
        var notFound = await _f.SendAsync(HttpMethod.Post, $"/api/v1/users/{unknown}/suspend");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _f.AuditRowsForAsync(unknown)).Should().BeEmpty();
    }

    [Fact]
    public async Task Nested_transaction_that_throws_rolls_back_its_writes_inside_an_outer_transaction()
    {
        var target = await _f.SeedUserAsync("savepoint@audit.test");

        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.users SET first_name = N'Outer' WHERE id = {target}", ct);

            try
            {
                await unitOfWork.ExecuteInTransactionAsync<bool>(async innerCt =>
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE dbo.users SET last_name = N'InnerShouldRollBack' WHERE id = {target}", innerCt);
                    throw new InvalidOperationException("inner step failed");
                }, ct);
            }
            catch (InvalidOperationException)
            {
                // The caller handles the failure and goes on, as ApproveAccessGrantRequest does.
            }

            return true;
        });

        using var verify = _f.Services.CreateScope();
        var user = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == target);
        user.FirstName.Should().Be("Outer");
        user.LastName.Should().Be("Seeded");
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private const string AdminHost = "admin.localhost";
        private const string Slug = "sec-audit";
        private const string OwnerEmail = "owner@sec-audit.test";
        private const string OwnerPassword = "OwnerPass@2026!";
        private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

        private readonly CapturingEmailService _email = new();
        private IntegrationTestEnvironmentScope _environmentScope = null!;
        private E2ETestFactory _factory = null!;
        private HttpClient _client = null!;

        public sealed record Session(Guid TenantId, Guid UserId, string Host, string Cookie, string Csrf);

        public Session Owner { get; private set; } = null!;
        public IServiceProvider Services => _factory.Services;

        public async Task InitializeAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("CROWNUS_TEST_DB");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
            else
                await AdminTestFactory.MigrateDatabaseAsync(connectionString);

            _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
            _factory = new E2ETestFactory(connectionString, _email);
            _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = false
            });

            await WaitForSeedersAsync();
            var admin = await PlatformAdminTestSignIn.SignInAsync(
                _client, "test_admin@onevo.dev", "test_password_123", AdminHost);
            Owner = await ProvisionAndLoginOwnerAsync(admin.SessionCookie, admin.CsrfToken);
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            _factory.Dispose();
            await _environmentScope.DisposeAsync();
        }

        /// <summary>A plain, active user in the owner's tenant, with no roles.</summary>
        public async Task<Guid> SeedUserAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.NewGuid();
            db.Add(new User
            {
                Id = userId, TenantId = Owner.TenantId, Email = email, FirstName = "Audit", LastName = "Seeded",
                PasswordHash = hasher.Hash("Password123!"), IsActive = true, EmailVerified = true,
                MustChangePassword = false, PasswordSetByAdmin = false, CreatedAt = now, CreatedById = Owner.UserId
            });
            await db.SaveChangesAsync();
            return userId;
        }

        public async Task<List<AuditLog>> AuditRowsForAsync(Guid resourceId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == Owner.TenantId && a.ResourceId == resourceId)
                .ToListAsync();
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Host = Owner.Host;
            request.Headers.Add("Cookie", Owner.Cookie);
            request.Headers.Add("X-CSRF-Token", Owner.Csrf);
            return await _client.SendAsync(request);
        }

        private async Task<Session> ProvisionAndLoginOwnerAsync(string adminCookie, string adminCsrf)
        {
            var host = $"{Slug}.localhost";
            var createResponse = await SendRawJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", new
            {
                company_name = "Security Audit Co",
                slug = Slug,
                industry_profile = "technology",
                company_size_range = "11-50",
                legal_entity_name = "Security Audit Co",
                registration_number = $"PV-{Slug}",
                country = "LK",
                timezone = "Asia/Colombo",
                currency = "LKR",
                subscription = new { plan_id = SeededPlanId, billing_cycle = "monthly", commercial_model = "standard" },
                owner_invite = new
                {
                    email = OwnerEmail, first_name = "Test", last_name = "Owner", completion_methods = new[] { "password" }
                }
            }, adminCookie, adminCsrf, Guid.NewGuid().ToString());
            var createJson = await ReadJsonAsync(createResponse);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
            var tenantId = createJson.GetProperty("tenantId").GetGuid();

            var inviteToken = await WaitForInviteTokenAsync();
            inviteToken.Should().NotBeNullOrEmpty();

            var accept = await SendRawJsonAsync(HttpMethod.Post, host, $"/api/v1/auth/invitations/{inviteToken}/accept-password", new
            {
                password = OwnerPassword,
                confirm_password = OwnerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
            accept.StatusCode.Should().Be(HttpStatusCode.OK);

            var confirm = await SendRawJsonAsync(HttpMethod.Patch, AdminHost, $"/admin/v1/tenants/{tenantId}/provision/confirm",
                new { confirm = true }, adminCookie, adminCsrf);
            confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var login = await SendRawJsonAsync(HttpMethod.Post, "localhost", "/api/v1/auth/login",
                new { email = OwnerEmail, password = OwnerPassword });
            var loginJson = await ReadJsonAsync(login);
            login.StatusCode.Should().Be(HttpStatusCode.Accepted, loginJson.ToString());
            var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
            var code = QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

            var exchange = await SendRawJsonAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange", new { code });
            exchange.StatusCode.Should().Be(HttpStatusCode.OK, await exchange.Content.ReadAsStringAsync());
            var cookies = ParseSetCookies(exchange);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = await db.Users.Where(u => u.TenantId == tenantId && u.Email == OwnerEmail).Select(u => u.Id).SingleAsync();

            return new Session(
                tenantId,
                userId,
                host,
                $"crownus_session={cookies["crownus_session"]}; crownus_csrf={cookies["crownus_csrf"]}",
                Uri.UnescapeDataString(cookies["crownus_csrf"]));
        }

        private async Task<string?> WaitForInviteTokenAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var template in _email.Templates)
                {
                    if (template.TemplateId == "tenant_owner_invite"
                        && string.Equals(template.To, OwnerEmail, StringComparison.OrdinalIgnoreCase)
                        && template.Data.TryGetProperty("invite_token", out var token))
                    {
                        return token.GetString();
                    }
                }
                await Task.Delay(250);
            }
            return null;
        }

        private async Task WaitForSeedersAsync()
        {
            await using (var migrateScope = _factory.Services.CreateAsyncScope())
            {
                await migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            }

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                try
                {
                    if (await db.Set<Permission>().AnyAsync(p => p.Code == "users:manage")
                        && await db.Set<SubscriptionPlan>().AnyAsync(p => p.Id == SeededPlanId))
                    {
                        return;
                    }
                }
                catch
                {
                    // Seeders still running.
                }
                await Task.Delay(250);
            }

            throw new TimeoutException("Seeders did not finish within 60s (permissions / subscription plan missing).");
        }

        private async Task<HttpResponseMessage> SendRawJsonAsync(
            HttpMethod method, string host, string path, object? body,
            string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Host = host;
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            if (csrfToken is not null) request.Headers.Add("X-CSRF-Token", csrfToken);
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await _client.SendAsync(request);
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }

        private static Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
        {
            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!response.Headers.TryGetValues("Set-Cookie", out var values))
                return cookies;
            foreach (var raw in values)
            {
                var pair = raw.Split(';', 2)[0];
                var idx = pair.IndexOf('=');
                if (idx > 0)
                    cookies[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
            }
            return cookies;
        }
    }
}
