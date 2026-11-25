using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.E2E;

/// <summary>
/// "Add yourself as an employee" against a real database: a brand-new tenant owner (a login with
/// no employee profile) sets up a department and a position, then onboards their own login.
/// The unit tests mock every repository, so only this catches database keys the self path can
/// collide with - for example the owner's existing user_roles row.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class OwnerAddsSelfAsEmployeeE2ETests : IAsyncLifetime
{
    private const string Slug = "self-e2e";
    private const string TenantHost = Slug + ".localhost";
    private const string AdminHost = "admin.localhost";
    private const string OwnerEmail = "owner@self-e2e.test";
    private const string OwnerPassword = "OwnerPass@2026!";

    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private string _ownerCookie = null!;
    private string _ownerCsrf = null!;

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
        var adminSession = await PlatformAdminTestSignIn.SignInAsync(
            _client, "test_admin@onevo.dev", "test_password_123", AdminHost);
        _adminCsrfToken = adminSession.CsrfToken;
        _adminCookie = adminSession.SessionCookie;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Owner_can_add_themselves_as_an_employee(bool positionGrantsTheOwnersOwnRole, bool withOnboardingChecklist)
    {
        await SignInNewOwnerAsync();

        var legalEntityId = FirstId(await GetJsonAsync("/api/v1/org/legal-entities"));

        var department = await PostOkAsync($"/api/v1/org/legal-entities/{legalEntityId}/departments",
            new { name = "Software Engineering", code = "SE" });
        var departmentId = department.GetProperty("id").GetGuid();

        var position = await PostOkAsync($"/api/v1/org/legal-entities/{legalEntityId}/positions",
            new { departmentId, name = "Software Engineer", code = "SWE", maxOccupancy = 5 });
        var positionId = position.GetProperty("id").GetGuid();

        if (positionGrantsTheOwnersOwnRole)
        {
            // The position's System Access Role is the role the owner already holds.
            var roles = await GetJsonAsync("/api/v1/roles");
            var ownerRoleId = roles.EnumerateArray()
                .Single(role => role.GetProperty("name").GetString() == "Owner")
                .GetProperty("id").GetGuid();
            var setAccess = await SendAsync(HttpMethod.Put,
                $"/api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/access",
                new { roleId = ownerRoleId, requiresApproval = false });
            setAccess.StatusCode.Should().Be(HttpStatusCode.OK, await setAccess.Content.ReadAsStringAsync());
        }

        var workModeId = FirstId(await GetJsonAsync($"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes"));

        Guid? templateId = null;
        if (withOnboardingChecklist)
        {
            // The owner's checklist tasks become Work tasks in the Office project, which needs the
            // person finalizing to be an employee - and here that is the very employee being created.
            var template = await PostOkAsync("/api/v1/people/checklist-templates", new
            {
                name = "Welcome",
                templateType = "onboarding",
                legalEntityId,
                tasks = new[]
                {
                    new { title = "Read the handbook", ownerType = "employee", dueOffsetDays = 1, sequence = 1, isRequired = true },
                },
            });
            templateId = template.GetProperty("id").GetGuid();
        }

        var draft = await PostOkAsync("/api/v1/onboarding/drafts", new
        {
            firstName = "Self",
            lastName = "Owner",
            workEmail = OwnerEmail,
            legalEntityId,
            departmentId,
            positionId,
            employmentType = "full_time",
            startDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            employeeNumber = "SE-0001",
            workModeId,
            selectedTemplateId = templateId,
            lastSavedStep = "review",
        });
        var draftId = draft.GetProperty("id").GetGuid();

        var finalize = await SendAsync(HttpMethod.Post, $"/api/v1/onboarding/drafts/{draftId}/finalize", body: null);
        var finalizeBody = await finalize.Content.ReadAsStringAsync();
        finalize.StatusCode.Should().Be(HttpStatusCode.OK, finalizeBody);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ownerUserId = await db.Users.IgnoreQueryFilters()
            .Where(user => user.Email == OwnerEmail).Select(user => user.Id).SingleAsync();
        var employeeIds = await db.Employees.IgnoreQueryFilters()
            .Where(employee => employee.UserId == ownerUserId).Select(employee => employee.Id).ToListAsync();
        employeeIds.Should().HaveCount(1, "the owner's own login now has exactly one employee profile");

        if (withOnboardingChecklist)
        {
            var checklist = await db.EmployeeChecklistTasks.IgnoreQueryFilters()
                .Where(task => task.EmployeeId == employeeIds[0]).ToListAsync();
            checklist.Should().ContainSingle();
            checklist[0].WorkTaskId.Should().NotBeNull("the checklist task is also a Work task in the Office project");
        }
    }

    private async Task SignInNewOwnerAsync()
    {
        var created = await SendAsync(HttpMethod.Post, "/admin/v1/tenants", CreateTenantBody(), host: AdminHost,
            cookie: _adminCookie, csrf: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createdBody = await ReadJsonAsync(created);
        created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody.ToString());
        var tenantId = createdBody.GetProperty("tenantId").GetGuid();

        var token = await WaitForInviteTokenAsync();
        token.Should().NotBeNullOrEmpty();

        var accept = await SendAsync(HttpMethod.Post, $"/api/v1/auth/invitations/{token}/accept-password", new
        {
            password = OwnerPassword,
            confirm_password = OwnerPassword,
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        }, cookie: null);
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var cookies = ParseSetCookies(accept);

        var confirm = await SendAsync(HttpMethod.Patch, $"/admin/v1/tenants/{tenantId}/provision/confirm",
            new { confirm = true }, host: AdminHost, cookie: _adminCookie, csrf: _adminCsrfToken);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A seat policy, as a live tenant has; without one finalize refuses with 422.
        var seats = await SendAsync(HttpMethod.Patch, $"/admin/v1/tenants/{tenantId}/subscription",
            new { included_seats = 10, overage_allowed = false }, host: AdminHost, cookie: _adminCookie, csrf: _adminCsrfToken);
        seats.IsSuccessStatusCode.Should().BeTrue(await seats.Content.ReadAsStringAsync());

        _ownerCsrf = Uri.UnescapeDataString(cookies["crownus_csrf"]);
        _ownerCookie = $"crownus_session={cookies["crownus_session"]}; crownus_csrf={cookies["crownus_csrf"]}";
    }

    private static object CreateTenantBody() => new
    {
        company_name = "Self E2E Company",
        slug = Slug,
        industry_profile = "technology",
        company_size_range = "51-200",
        legal_entity_name = "Self E2E (Pvt) Ltd",
        registration_number = "PV88888",
        country = "LK",
        timezone = "Asia/Colombo",
        currency = "LKR",
        subscription = new { plan_id = SeededPlanId, billing_cycle = "monthly", commercial_model = "standard" },
        owner_invite = new
        {
            email = OwnerEmail,
            first_name = "Self",
            last_name = "Owner",
            completion_methods = new[] { "password" }
        }
    };

    /// <summary>The id of the first row of a list response, whether it is a bare array or wrapped in items.</summary>
    private static Guid FirstId(JsonElement list)
    {
        var rows = list.ValueKind == JsonValueKind.Array ? list : list.GetProperty("items");
        rows.GetArrayLength().Should().BeGreaterThan(0, list.ToString());
        return rows[0].GetProperty("id").GetGuid();
    }

    private async Task<string?> WaitForInviteTokenAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var token = _email.LastInviteToken();
            if (!string.IsNullOrEmpty(token))
                return token;
            await Task.Delay(250);
        }
        return null;
    }

    private async Task WaitForSeedersAsync()
    {
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var permissionsReady = await db.Set<ONEVO.Domain.Features.Auth.Entities.Permission>().AnyAsync();
                var planReady = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>()
                    .AnyAsync(p => p.Id == SeededPlanId);
                if (permissionsReady && planReady)
                    return;
            }
            catch
            {
                // Schema not created yet; keep polling.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        var response = await SendAsync(HttpMethod.Get, path, body: null);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path}: {json}");
        return json;
    }

    private async Task<JsonElement> PostOkAsync(string path, object body)
    {
        var response = await SendAsync(HttpMethod.Post, path, body);
        var json = await ReadJsonAsync(response);
        ((int)response.StatusCode).Should().BeInRange(200, 201, $"{path}: {json}");
        return json;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body)
        => SendAsync(method, path, body, cookie: _ownerCookie, host: TenantHost, csrf: _ownerCsrf);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, string? cookie, string host = TenantHost,
        string? csrf = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrf is not null)
            request.Headers.Add("X-CSRF-Token", csrf);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
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
