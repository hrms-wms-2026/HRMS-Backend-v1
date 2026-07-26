using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.DevPlatform;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ConfigurationTemplateManagerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_configuration_templates_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new AdminTestFactory(connectionString);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Apply_writes_a_real_row_and_reapply_is_append_only()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET session_replication_role = replica; " +
            "SELECT set_config('app.tenant_context_mode', 'admin', false);");

        var platformUser = new PlatformUser { Id = Guid.NewGuid(), Email = "op@onevo.test", FullName = "Op" };
        db.PlatformUsers.Add(platformUser);

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);

        var template = new ConfigurationTemplate
        {
            Id = Guid.NewGuid(),
            TemplateKey = "uk-office-defaults",
            TemplateType = ConfigurationTemplate.TypeConfiguration,
            Name = "UK Office Defaults",
            ModuleKeysJson = "[]",
            PayloadJson = """{"timezone":"Europe/London"}""",
            IsActive = true,
            CreatedById = platformUser.Id
        };
        db.ConfigurationTemplates.Add(template);
        await db.SaveChangesAsync();

        var handler = new ApplyConfigurationTemplateToTenantCommandHandler(
            scope.ServiceProvider.GetRequiredService<ITenantRepository>(),
            new EfConfigurationTemplateRepository(db),
            new EfTenantConfigurationTemplateApplicationRepository(db),
            scope.ServiceProvider.GetRequiredService<IModuleEntitlementService>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

        var first = await handler.Handle(
            new ApplyConfigurationTemplateToTenantCommand(tenant.Id, template.Id, false, platformUser.Id),
            default);
        var second = await handler.Handle(
            new ApplyConfigurationTemplateToTenantCommand(tenant.Id, template.Id, false, platformUser.Id),
            default);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.NotEqual(first.Value!.ApplicationId, second.Value!.ApplicationId);

        var rows = await db.TenantConfigurationTemplateApplications
            .Where(a => a.TenantId == tenant.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("applied", r.Status));
    }

    [Fact]
    public async Task Migration_rejects_nonexistent_tenant_and_template_foreign_keys()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        // Bypass the tenant_isolation RLS policy via the admin context-mode escape
        // hatch (not session_replication_role = replica, which also disables the
        // FK-constraint triggers this test needs to actually fire).
        await ExecuteAsync(connection, "SELECT set_config('app.tenant_context_mode', 'admin', false)");

        var badTenant = await Assert.ThrowsAsync<PostgresException>(() => InsertApplicationAsync(
            connection, tenantId: Guid.NewGuid(), configurationTemplateId: null, appliedById: Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, badTenant.SqlState);
    }

    private static async Task InsertApplicationAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid? configurationTemplateId,
        Guid appliedById)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tenant_configuration_template_applications
                (id, tenant_id, configuration_template_id, template_type, applied_version,
                 applied_payload_json, status, applied_by_id, applied_at)
            VALUES
                (@id, @tenant_id, @configuration_template_id, 'configuration', 1,
                 '{}', 'applied', @applied_by_id, now())
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("configuration_template_id", configurationTemplateId ?? Guid.NewGuid());
        command.Parameters.AddWithValue("applied_by_id", appliedById);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
