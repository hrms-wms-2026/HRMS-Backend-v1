using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Billing;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class InvoicesAdminApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("onevo_invoice_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _tenantId;

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
        _tenantId = await SeedTenantAsync();
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

    private async Task<Guid> SeedTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Invoice Test Tenant",
            Slug = "invoice-test-tenant",
            IndustryProfile = "professional_services",
            CompanySizeRange = "11-50",
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task InvoiceLifecycle_CreateListDetailMarkPaid()
    {
        var createBody = new
        {
            tenant_id = _tenantId,
            currency = "USD",
            subtotal_amount = 100m,
            tax_amount = 10m,
            discount_amount = 5m,
            status = "open"
        };

        var createResp = await _client.PostAsJsonAsync("/admin/v1/invoices", createBody);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await ReadJsonAsync(createResp);
        var invoiceId = created.GetProperty("id").GetGuid();
        created.GetProperty("total_amount").GetDecimal().Should().Be(105m);
        created.GetProperty("status").GetString().Should().Be("open");
        created.GetProperty("issued_at").ValueKind.Should().NotBe(JsonValueKind.Null);

        var listResp = await _client.GetAsync($"/admin/v1/invoices?tenant_id={_tenantId}&status=open");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await ReadJsonAsync(listResp);
        list.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        list.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("id").GetGuid() == invoiceId)
            .Should().BeTrue();

        var tenantListResp = await _client.GetAsync($"/admin/v1/tenants/{_tenantId}/invoices");
        tenantListResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detailResp = await _client.GetAsync($"/admin/v1/invoices/{invoiceId}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadJsonAsync(detailResp);
        detail.GetProperty("audit_logs").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        detail.GetProperty("audit_logs")[0].GetProperty("action").GetString()
            .Should().Be("invoice.created");

        var markPaidResp = await _client.PatchAsync($"/admin/v1/invoices/{invoiceId}/mark-paid", null);
        markPaidResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var paid = await ReadJsonAsync(markPaidResp);
        paid.GetProperty("status").GetString().Should().Be("paid");
        paid.GetProperty("paid_at").ValueKind.Should().NotBe(JsonValueKind.Null);
        paid.GetProperty("audit_logs").EnumerateArray()
            .Any(l => l.GetProperty("action").GetString() == "invoice.marked_paid")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvoiceLifecycle_CreateAndVoid()
    {
        var createResp = await _client.PostAsJsonAsync("/admin/v1/invoices", new
        {
            tenant_id = _tenantId,
            currency = "USD",
            subtotal_amount = 50m,
            tax_amount = 0m,
            discount_amount = 0m,
            status = "draft"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await ReadJsonAsync(createResp);
        var invoiceId = created.GetProperty("id").GetGuid();

        var voidResp = await _client.PatchAsync($"/admin/v1/invoices/{invoiceId}/void", null);
        voidResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var voided = await ReadJsonAsync(voidResp);
        voided.GetProperty("status").GetString().Should().Be("void");
        voided.GetProperty("voided_at").ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}
