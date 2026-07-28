using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Tenancy;

[Collection(WebApplicationFactoryCollection.Name)]
public class TenantsAdminApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("onevo_admin_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _planId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();
        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        _factory = new AdminTestFactory(connectionString);

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

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

        _planId = await SeedPlanAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    // -- Helpers ----------------------------------------------------------------

    private static JsonSerializerOptions JsonOpts => new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // Matches the current CreateTenantRequest contract: company profile fields plus a
    // subscription referencing a real subscription_plans row (seeded in InitializeAsync).
    private object DraftBody(string slug = "acme-co", string name = "Acme Co") => new
    {
        company_name = name,
        slug,
        industry_profile = "professional_services",
        company_size_range = "11-50",
        legal_entity_name = $"{name} (Pvt) Ltd",
        country = "LK",
        timezone = "Asia/Colombo",
        currency = "LKR",
        subscription = new
        {
            plan_id = _planId,
            billing_cycle = "monthly",
            commercial_model = "per_user"
        }
    };

    private async Task<Guid> SeedPlanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Integration Test Plan",
            Code = "int-test-plan",
            Tier = "standard",
            IncludedModulesJson = "[]",
            CompanySizeRange = "11-50",
            CalculatedMonthlyPrice = 100m,
            CalculatedAnnualPrice = 1000m,
            Currency = "USD",
            TrialPeriodDays = 30,
            UnpaidGracePeriodDays = 7,
            IsActive = true
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    // Tenant creation seeds a system "Owner" role; the invite endpoint requires a
    // role_id in the request but always assigns this Owner role.
    private Guid GetOwnerRoleId(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Roles.Single(r => r.TenantId == tenantId && r.Name == "Owner").Id;
    }

    private async Task<Guid> CreateDraftAsync(string slug = "acme-co", string name = "Acme Co")
    {
        var resp = await _client.PostAsJsonAsync("/admin/v1/tenants", DraftBody(slug, name));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadJsonAsync(resp);
        return body.GetProperty("tenantId").GetGuid();
    }

    // -- Tests ------------------------------------------------------------------

    [Fact]
    public async Task PostTenants_CreatesDraft_InProvisioningStatus()
    {
        var resp = await _client.PostAsJsonAsync("/admin/v1/tenants", DraftBody("draft-co", "Draft Co"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("status").GetString().Should().Be("provisioning");
        body.GetProperty("tenantId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetTenants_ListsProvisioningTenant()
    {
        await CreateDraftAsync("list-co", "List Co");

        var resp = await _client.GetAsync("/admin/v1/tenants?status=provisioning&page=1&page_size=10");

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
        body.GetProperty("status").GetString().Should().Be("provisioning");
        body.GetProperty("industry_profile").GetString().Should().Be("professional_services");
    }

    [Fact]
    public async Task PatchTenant_EditsName_WhileProvisioning()
    {
        var tenantId = await CreateDraftAsync("edit-co", "Edit Co");

        var patchResp = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}",
            new { name = "Edit Co Renamed" });

        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/admin/v1/tenants/{tenantId}");
        var body = await ReadJsonAsync(get);
        body.GetProperty("company_name").GetString().Should().Be("Edit Co Renamed");
    }

    [Fact]
    public async Task PatchTenantStatus_SuspendThenUnsuspend_TransitionsStatus()
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

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status.Should().Be(TenantStatus.Suspended);

            // Suspend must write exactly one status-history row with the transition details.
            var histories = db.TenantStatusHistories
                .Where(h => h.TenantId == tenantId)
                .OrderBy(h => h.ChangedAt)
                .ToList();
            histories.Should().HaveCount(1);
            histories[0].TenantId.Should().Be(tenantId);
            histories[0].FromStatus.Should().Be(TenantStatus.Active);
            histories[0].ToStatus.Should().Be(TenantStatus.Suspended);
            histories[0].Reason.Should().Be("test_only");
            histories[0].ChangedById.Should().NotBeNull();
            histories[0].ChangedById.Should().NotBe(Guid.Empty);
            histories[0].ChangedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        }

        var unsuspend = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/status",
            new { action = "unsuspend" });
        unsuspend.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status.Should().Be(TenantStatus.Active);

            // Unsuspend appends a second history row.
            var histories = db.TenantStatusHistories
                .Where(h => h.TenantId == tenantId)
                .OrderBy(h => h.ChangedAt)
                .ToList();
            histories.Should().HaveCount(2);
            histories[1].FromStatus.Should().Be(TenantStatus.Suspended);
            histories[1].ToStatus.Should().Be(TenantStatus.Active);
        }
    }

    [Fact]
    public async Task PatchTenantStatus_ValidationFailure_WritesNoHistory()
    {
        var tenantId = await CreateDraftAsync("badstatus-co", "Bad Status Co");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status = TenantStatus.Active;
            await db.SaveChangesAsync();
        }

        // suspend without a reason fails validation.
        var resp = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/status",
            new { action = "suspend" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await ReadJsonAsync(resp);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Validation Error");
        body.GetProperty("detail").GetString().Should().Be("One or more validation errors occurred.");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("errors").GetProperty("Reason").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("reason is required when suspending a tenant.");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantStatusHistories.Count(h => h.TenantId == tenantId).Should().Be(0);
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status.Should().Be(TenantStatus.Active);
        }
    }

    [Fact]
    public async Task PatchTenantStatus_Unauthorized_WritesNoHistory()
    {
        var tenantId = await CreateDraftAsync("noauth-co", "No Auth Co");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status = TenantStatus.Active;
            await db.SaveChangesAsync();
        }

        // Client without the admin session cookie or CSRF token.
        using var anonClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        var resp = await anonClient.PatchAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/status",
            new { action = "suspend", reason = "should_not_apply" });
        resp.IsSuccessStatusCode.Should().BeFalse();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantStatusHistories.Count(h => h.TenantId == tenantId).Should().Be(0);
            var t = await db.Tenants.FindAsync(tenantId);
            t!.Status.Should().Be(TenantStatus.Active);
        }
    }

    [Fact]
    public async Task PostInviteAdmin_CreatesUserRoleAndInvitation()
    {
        var tenantId = await CreateDraftAsync("invite-co", "Invite Co");
        var ownerRoleId = GetOwnerRoleId(tenantId);

        var resp = await _client.PostAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/invite-admin",
            new
            {
                email = "owner@invite-co.example",
                first_name = "Owner",
                last_name = "Person",
                role_id = ownerRoleId
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("userId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("inviteExpiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.InvitationTokens.Count(i => i.TenantId == tenantId).Should().Be(1);
        db.UserRoles.Count(ur => ur.RoleId == ownerRoleId).Should().Be(1);
        db.Users.Count(u => u.TenantId == tenantId).Should().Be(1);
    }

    [Fact]
    public async Task GetProvisioningSummary_ReportsIncomplete_WhenSectionsMissing()
    {
        var tenantId = await CreateDraftAsync("summary-co", "Summary Co");

        var resp = await _client.GetAsync($"/admin/v1/tenants/{tenantId}/provisioning-summary");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("canActivate").GetBoolean().Should().BeFalse();

        var sections = body.GetProperty("sections");
        sections.GetProperty("ownerInvite").GetProperty("complete").GetBoolean().Should().BeFalse();

        var blockers = body.GetProperty("blockingErrors").EnumerateArray().ToList();
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
        body.GetProperty("canActivate").GetBoolean().Should().BeFalse();
        body.GetProperty("blockingErrors").EnumerateArray().Should().NotBeEmpty();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TenantStatusHistories.Count(h => h.TenantId == tenantId).Should().Be(0);
    }

    [Fact]
    public async Task ProvisionConfirm_TenantNotFound_Returns404_AndWritesNoHistory()
    {
        var missingTenantId = Guid.NewGuid();

        var resp = await _client.PatchAsJsonAsync(
            $"/admin/v1/tenants/{missingTenantId}/provision/confirm",
            new { confirm = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TenantStatusHistories.Count(h => h.TenantId == missingTenantId).Should().Be(0);
    }

    [Fact]
    public async Task ProvisionConfirm_Returns204_WhenAllSectionsCompleteAndInviteExists()
    {
        var tenantId = await CreateDraftAsync("activate-co", "Activate Co");
        var ownerRoleId = GetOwnerRoleId(tenantId);

        var invite = await _client.PostAsJsonAsync(
            $"/admin/v1/tenants/{tenantId}/invite-admin",
            new
            {
                email = "owner@activate-co.example",
                first_name = "Owner",
                last_name = "Person",
                role_id = ownerRoleId
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
