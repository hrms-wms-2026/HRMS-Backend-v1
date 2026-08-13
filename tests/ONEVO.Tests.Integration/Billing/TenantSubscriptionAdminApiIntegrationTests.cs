using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Billing;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TenantSubscriptionAdminApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("onevo_tenant_subscription_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _tenantId;
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

        await AuthenticateAsync();
        (_tenantId, _planId) = await SeedTenantWithSubscriptionAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        loginResponse.IsSuccessStatusCode.Should().BeTrue(
            await loginResponse.Content.ReadAsStringAsync());

        var cookies = loginResponse.Headers.GetValues("Set-Cookie");
        var adminSessionCookie = cookies.FirstOrDefault(c => c.StartsWith("admin_session="));
        var csrfCookie = cookies.FirstOrDefault(c => c.StartsWith("admin_csrf="));

        if (adminSessionCookie != null)
            _client.DefaultRequestHeaders.Add("Cookie", adminSessionCookie.Split(';')[0]);
        if (csrfCookie != null)
        {
            var csrfToken = csrfCookie.Split(';')[0].Split('=')[1];
            _client.DefaultRequestHeaders.Add("X-CSRF-Token", csrfToken);
        }
    }

    private async Task<(Guid TenantId, Guid PlanId)> SeedTenantWithSubscriptionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Integration Plan",
            Code = "integration-plan",
            Tier = "standard",
            IncludedModulesJson = "[]",
            CompanySizeRange = "11-50",
            CalculatedMonthlyPrice = 120m,
            CalculatedAnnualPrice = 1200m,
            Currency = "USD",
            TrialPeriodDays = 30,
            UnpaidGracePeriodDays = 7,
            IsActive = true
        };

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Subscription Test Tenant",
            Slug = "subscription-test-tenant",
            IndustryProfile = "professional_services",
            CompanySizeRange = "11-50",
            Status = TenantStatus.Active,
            SubscriptionPlanId = plan.Id,
            CreatedAt = now
        };

        var subscription = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = plan.Id,
            BillingCycle = "monthly",
            Status = "active",
            BillingCurrency = "USD",
            CalculatedMonthlyPrice = 120m,
            CalculatedAnnualPrice = 1200m,
            CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime),
            CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime),
            CompanySizeRange = "11-50",
            SelectedModulesJson = "[]",
            UnpaidGracePeriodDays = 7,
            CreatedAt = now
        };

        db.SubscriptionPlans.Add(plan);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return (tenant.Id, plan.Id);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task GetTenantSubscription_ReturnsDetailForSeededTenant()
    {
        var response = await _client.GetAsync($"/admin/v1/tenants/{_tenantId}/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(response);
        body.GetProperty("tenant_id").GetGuid().Should().Be(_tenantId);
        body.GetProperty("tenant_name").GetString().Should().Be("Subscription Test Tenant");
        body.GetProperty("subscription_plan_id").GetGuid().Should().Be(_planId);
        body.GetProperty("plan_name").GetString().Should().Be("Integration Plan");
        body.GetProperty("plan_code").GetString().Should().Be("integration-plan");
        body.GetProperty("status").GetString().Should().Be("active");
        body.GetProperty("amount").GetDecimal().Should().Be(120m);
        body.GetProperty("is_active_access").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetTenantSubscription_UnknownTenant_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/admin/v1/tenants/{Guid.NewGuid()}/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
