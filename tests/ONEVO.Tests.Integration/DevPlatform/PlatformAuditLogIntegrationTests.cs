using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.DevPlatform;

/// <summary>
/// End to end through the real API, pipeline and database: an audited admin command writes exactly
/// one platform_audit_logs row in the same request, a failed one writes none, the row is readable
/// through the read-only endpoint, and the runtime role (onevo_app) cannot change or remove it.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class PlatformAuditLogIntegrationTests : IAsyncLifetime
{
    private const string AdminEmail = "test_admin@onevo.dev";
    private const string AdminPassword = "test_password_123";

    private string _connectionString = string.Empty;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(_connectionString);
        _factory = new AdminTestFactory(_connectionString);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    private async Task<(string SessionCookie, string CsrfToken)> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/admin/v1/auth/login", new { email = AdminEmail, password = AdminPassword });
        response.EnsureSuccessStatusCode();

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var session = cookies.First(c => c.StartsWith("admin_session=")).Split(';')[0];
        var csrf = cookies.First(c => c.StartsWith("admin_csrf=")).Split(';')[0].Split('=', 2)[1];
        return (session, csrf);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string sessionCookie, string? csrf = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", sessionCookie);
        if (csrf is not null)
            request.Headers.Add("X-CSRF-Token", csrf);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<Guid> SeedTenantAsync(string name, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            CompanySizeRange = "51-200",
            IndustryProfile = "retail",
            Status = TenantStatus.Provisioning,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<List<ONEVO.Domain.Features.DevPlatform.Audit.Entities.PlatformAuditLog>> AuditRowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PlatformAuditLogs.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task AuditedAdminCommand_WritesExactlyOneRow_ReadableThroughTheEndpoint()
    {
        var tenantId = await SeedTenantAsync("Audit Old Name", "audit-old-slug");
        var (session, csrf) = await LoginAsync();

        var update = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/admin/v1/tenants/{tenantId}", session, csrf,
            new { name = "Audit New Name", slug = (string?)null, industryProfile = (string?)null }));
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await AuditRowsAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.Action.Should().Be("tenant.updated");
        row.TargetType.Should().Be("tenant");
        row.TargetId.Should().Be(tenantId.ToString());
        row.TargetTenantId.Should().Be(tenantId);
        row.ActorPlatformUserId.Should().NotBeNull();
        row.BeforeJson.Should().Contain("Audit Old Name");
        row.AfterJson.Should().Contain("Audit New Name");
        row.CorrelationId.Should().NotBeNullOrEmpty();

        var list = await _client.SendAsync(Request(HttpMethod.Get, $"/admin/v1/platform-audit-log?target_tenant_id={tenantId}", session));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var json = body.RootElement.GetRawText();
        json.Should().Contain("tenant.updated").And.Contain(AdminEmail);
    }

    [Fact]
    public async Task FailedAdminCommand_WritesNoRow()
    {
        var (session, csrf) = await LoginAsync();

        var update = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/admin/v1/tenants/{Guid.NewGuid()}", session, csrf,
            new { name = "Nobody", slug = (string?)null, industryProfile = (string?)null }));
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RuntimeRole_CanInsertAndRead_ButCannotUpdateOrDeleteAuditRows()
    {
        var runtime = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = "onevo_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword,
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(runtime);
        await connection.OpenAsync();

        var id = Guid.NewGuid();
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO platform_audit_logs (id, action, target_type, created_at)
                VALUES (@id, 'test.inserted', 'test', now());
                """;
            insert.Parameters.AddWithValue("id", id);
            (await insert.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT count(*) FROM platform_audit_logs WHERE id = @id;";
            select.Parameters.AddWithValue("id", id);
            ((long)(await select.ExecuteScalarAsync())!).Should().Be(1);
        }

        foreach (var sql in new[]
                 {
                     "UPDATE platform_audit_logs SET action = 'tampered' WHERE id = @id;",
                     "DELETE FROM platform_audit_logs WHERE id = @id;",
                     "TRUNCATE platform_audit_logs;",
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("id", id);
            var act = () => command.ExecuteNonQueryAsync();
            (await act.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, sql);
        }
    }
}
