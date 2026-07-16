using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public class TenantsAdminApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("onevo_admin_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new AdminTestFactory(_postgres.GetConnectionString());

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        // Allow PermissionSeeder + EnsureCreated to finish.
        await Task.Delay(500);

        var loginResponse = await _client.PostAsJsonAsync("/admin/v1/auth/login", new { email = "test_admin@onevo.dev", password = "test_password_123" });
        if (!loginResponse.IsSuccessStatusCode) { throw new Exception(await loginResponse.Content.ReadAsStringAsync()); }
        var cookies = loginResponse.Headers.GetValues("Set-Cookie");
        var adminSessionCookie = cookies.FirstOrDefault(c => c.StartsWith("admin_session="));
        var csrfCookie = cookies.FirstOrDefault(c => c.StartsWith("admin_csrf="));
        
        if (adminSessionCookie != null)
        {
            var cookieValue = adminSessionCookie.Split(';')[0];
            _client.DefaultRequestHeaders.Add("Cookie", cookieValue);
        }
        if (csrfCookie != null)
        {
            var csrfToken = csrfCookie.Split(';')[0].Split('=')[1];
            _client.DefaultRequestHeaders.Add("X-CSRF-Token", csrfToken);
        }
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static JsonSerializerOptions JsonOpts => new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private object DraftBody(string slug = "acme-co", string name = "Acme Co") => new
    {
        name,
        slug,
        industry_profile = "professional_services"
    };

    private async Task<Guid> SeedRoleAsync(Guid tenantId, string name = "Tenant Owner")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = "Owner role created for the integration test.",
            IsSystem = false,
            IsDeleted = false
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task<Guid> CreateDraftAsync(string slug = "acme-co", string name = "Acme Co")
    {
        var resp = await _client.PostAsJsonAsync("/admin/v1/tenants", DraftBody(slug, name));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadJsonAsync(resp);
        return body.GetProperty("tenant_id").GetGuid();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostTenants_CreatesDraft_InTrialStatus()
    {
        var resp = await _client.PostAsJsonAsync("/admin/v1/tenants", DraftBody("draft-co", "Draft Co"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("status").GetString().Should().Be("trial");
        body.GetProperty("tenant_id").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetTenants_ListsTrialTenant()
    {
        await CreateDraftAsync("list-co", "List Co");

        var resp = await _client.GetAsync("/admin/v1/tenants?status=trial&page=1&page_size=10");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("slug").GetString() == "list-co");
    }

    [Fact]
    public async Task GetValidate_ReturnsConflict_OnTakenSlug()
    {
        await CreateDraftAsync("taken-slug", "Taken Slug Co");

        var resp = await _client.GetAsync("/admin/v1/tenants/validate?slug=taken-slug");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("valid").GetBoolean().Should().BeFalse();
        var conflicts = body.GetProperty("conflicts").EnumerateArray().ToList();
        conflicts.Should().Contain(c => c.GetProperty("field").GetString() == "slug");
    }

    [Fact]
    public async Task GetTenantById_ReturnsDraftDetail()
    {
        var tenantId = await CreateDraftAsync("detail-co", "Detail Co");

        var resp = await _client.GetAsync($"/admin/v1/tenants/{tenantId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("id").GetGuid().Should().Be(tenantId);
        body.GetProperty("status").GetString().Should().Be("trial");
        body.GetProperty("industry_profile").GetString().Should().Be("professional_services");
    }

    [Fact]
    public async Task PatchTenant_EditsName_WhenStillInTrial()
    {
        var tenantId = await CreateDraftAsync("edit-co", "Edit Co");

        var patchResp = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}",
            new { name = "Edit Co Renamed" });

        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/admin/v1/tenants/{tenantId}");
        var body = await ReadJsonAsync(get);
        body.GetProperty("name").GetString().Should().Be("Edit Co Renamed");
    }

    [Fact]
    public async Task PatchTenantStatus_SuspendThenUnsuspend_PersistsAuditTrail()
    {
        var tenantId = await CreateDraftAsync("status-co", "Status Co");

        // Force tenant active so it is eligible for suspend/unsuspend.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status = TenantStatus.Active;
            t.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var suspend = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/status",
            new { action = "suspend", reason = "test_only" });
        suspend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unsuspend = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/status",
            new { action = "unsuspend" });
        unsuspend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var historyCount = db.TenantStatusHistories.Count(h => h.TenantId == tenantId);
            historyCount.Should().Be(2);

            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status.Should().Be(TenantStatus.Active);
        }
    }

    [Fact]
    public async Task PostInviteAdmin_CreatesUserRoleAndInvitation()
    {
        var tenantId = await CreateDraftAsync("invite-co", "Invite Co");
        var roleId = await SeedRoleAsync(tenantId);

        var resp = await _client.PostAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/invite-admin",
            new
            {
                email = "owner@invite-co.example",
                first_name = "Owner",
                last_name = "Person",
                role_id = roleId
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("user_id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("invite_expires_at").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.InvitationTokens.Count(i => i.TenantId == tenantId).Should().Be(1);
        db.UserRoles.Count(ur => ur.RoleId == roleId).Should().Be(1);
        db.Users.Count(u => u.TenantId == tenantId).Should().Be(1);
    }

    [Fact]
    public async Task GetProvisioningSummary_ReportsIncomplete_WhenSectionsMissing()
    {
        var tenantId = await CreateDraftAsync("summary-co", "Summary Co");

        var resp = await _client.GetAsync($"/admin/v1/tenants/{tenantId}/provisioning-summary");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("can_activate").GetBoolean().Should().BeFalse();

        var sections = body.GetProperty("sections");
        sections.GetProperty("owner_invite").GetProperty("complete").GetBoolean().Should().BeFalse();

        var blockers = body.GetProperty("blocking_errors").EnumerateArray().ToList();
        blockers.Should().NotBeEmpty();
        blockers.Should().Contain(b => b.GetProperty("section").GetString() == "owner_invite");
    }

    [Fact]
    public async Task ProvisionConfirm_Returns422_WithSummary_WhenIncomplete()
    {
        var tenantId = await CreateDraftAsync("block-co", "Block Co");

        var resp = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/provision/confirm",
            new { confirm = true });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await ReadJsonAsync(resp);
        body.GetProperty("can_activate").GetBoolean().Should().BeFalse();
        body.GetProperty("blocking_errors").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task ProvisionConfirm_Returns204_WhenAllSectionsCompleteAndInviteExists()
    {
        var tenantId = await CreateDraftAsync("activate-co", "Activate Co");
        var roleId = await SeedRoleAsync(tenantId);

        var invite = await _client.PostAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/invite-admin",
            new
            {
                email = "owner@activate-co.example",
                first_name = "Owner",
                last_name = "Person",
                role_id = roleId
            });
        invite.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status = TenantStatus.Active;
            t.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var get = await _client.GetAsync($"/admin/v1/tenants/{tenantId}");
        var detail = await ReadJsonAsync(get);
        detail.GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public async Task TenantApi_RejectsAdminToken_WithoutTenantClaim()
    {
        var resp = await _client.GetAsync("/api/v1/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
