using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Full-stack proof that POST /api/v1/auth/forgot-password enforces RLS over real HTTP: the host
/// under test runs Program.cs's own AddInfrastructure(...) wiring (real onevo_app connection +
/// real TenantRlsInterceptor - see BaseForgotPasswordRestrictedRoleTestFactory's doc comment for
/// why this is NOT the same guarantee as BaseDomainForgotPasswordIntegrationTests, which runs on
/// BaseDomainLoginTestFactory's superuser-bound, interceptor-less ApplicationDbContext).
/// BaseForgotPasswordRlsIntegrationTests already proves the handler itself is RLS-safe by invoking
/// it directly; this class proves the same thing end-to-end through routing, host tenant
/// resolution, and the real HTTP pipeline via AuthPasswordController. Requires Docker.
/// </summary>
public sealed class BaseForgotPasswordRestrictedRoleHttpIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_forgot_password_restricted_http_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private string _adminConnectionString = null!;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private BaseForgotPasswordRestrictedRoleTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _adminConnectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(_adminConnectionString);

        _environmentScope = new IntegrationTestEnvironmentScope(_adminConnectionString);

        await GrantOnevoAppTablePrivilegesAsync();

        _factory = new BaseForgotPasswordRestrictedRoleTestFactory(_environmentScope.DefaultConnectionString);
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
        await _environmentScope.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task BaseDomain_OneEligibleTenant_RestrictedRoleHttp_CreatesTokenAndOutboxRowWithoutRlsViolation()
    {
        var (tenantId, userId, email) = await SeedActiveUserAsync("rr-fp-one", "rr-fp-one@test.onevo.dev");

        var response = await PostForgotPasswordAsync(host: "localhost", email);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a 42501 RLS violation under onevo_app would surface as a 500, not a 200");
        body.Should().NotContain("42501");
        body.Should().NotContainEquivalentOf("row-level security");
        body.Should().Contain("If the email exists, a reset link has been sent.");

        await using var adminDb = BuildAdminDbContext();
        var tokens = await adminDb.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();
        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantId);

        var payloads = await GetPasswordResetEmailPayloadsAsync(userId);
        payloads.Should().HaveCount(1);
        payloads[0].TenantId.Should().Be(tenantId);
        payloads[0].TenantSlug.Should().Be("rr-fp-one");
    }

    [Fact]
    public async Task BaseDomain_SameEmailInTwoTenants_RestrictedRoleHttp_CreatesTwoTokensAndTwoOutboxRowsWithCorrectTenantIds()
    {
        const string sharedEmail = "rr-fp-shared@test.onevo.dev";
        var (tenantAId, userAId, _) = await SeedActiveUserAsync("rr-fp-a", sharedEmail);
        var (tenantBId, userBId, _) = await SeedActiveUserAsync("rr-fp-b", sharedEmail);

        var response = await PostForgotPasswordAsync(host: "localhost", sharedEmail);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var adminDb = BuildAdminDbContext();
        var tokens = await adminDb.PasswordResetTokens
            .Where(t => t.UserId == userAId || t.UserId == userBId)
            .ToListAsync();
        tokens.Should().HaveCount(2);
        tokens.Single(t => t.UserId == userAId).TenantId.Should().Be(tenantAId);
        tokens.Single(t => t.UserId == userBId).TenantId.Should().Be(tenantBId);

        var payloadsA = await GetPasswordResetEmailPayloadsAsync(userAId);
        var payloadsB = await GetPasswordResetEmailPayloadsAsync(userBId);
        payloadsA.Should().HaveCount(1);
        payloadsB.Should().HaveCount(1);
        payloadsA[0].TenantSlug.Should().Be("rr-fp-a");
        payloadsB[0].TenantSlug.Should().Be("rr-fp-b");
    }

    [Fact]
    public async Task BaseDomain_NineTenantsOverflow_RestrictedRoleHttp_CreatesNoTokensOrOutboxRows()
    {
        const string sharedEmail = "rr-fp-overflow@test.onevo.dev";
        for (var i = 0; i < 9; i++)
            await SeedActiveUserAsync($"rr-fp-overflow-{i}", sharedEmail);

        var response = await PostForgotPasswordAsync(host: "localhost", sharedEmail);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var adminDb = BuildAdminDbContext();
        (await adminDb.PasswordResetTokens.AnyAsync()).Should().BeFalse(
            "overflow must never touch any candidate's tenant context or create a token");
        (await adminDb.Set<OutboxMessage>().AnyAsync(m => m.Type == OutboxMessageTypes.PasswordResetEmail))
            .Should().BeFalse();
    }

    private async Task<HttpResponseMessage> PostForgotPasswordAsync(string host, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password");
        request.Headers.Host = host;
        request.Content = JsonContent.Create(new { email });
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// IntegrationDatabaseBootstrap runs EF migrations over the Testcontainers superuser connection
    /// (never onevo_migrator), so the production ALTER DEFAULT PRIVILEGES step in
    /// ops/postgres/local-bootstrap-roles.sql never fires here and onevo_app ends up with no grants
    /// at all on the tables migrations created. This reproduces only the blanket fallback grant
    /// from that same script - onevo_app remains NOBYPASSRLS; this is an object-level ACL grant,
    /// not an RLS change. Identical to BaseForgotPasswordRlsIntegrationTests.GrantOnevoAppTablePrivilegesAsync.
    /// </summary>
    private async Task GrantOnevoAppTablePrivilegesAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            GRANT USAGE ON SCHEMA public TO onevo_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO onevo_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO onevo_app;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(Guid TenantId, Guid UserId, string Email)> SeedActiveUserAsync(string tenantSlug, string email)
    {
        await using var db = BuildAdminDbContext();

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
            PasswordHash = "irrelevant-hash",
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (tenant.Id, user.Id, user.Email);
    }

    /// <summary>Direct admin/superuser-connection context, bypassing RLS on purpose for seeding and verification.</summary>
    private ApplicationDbContext BuildAdminDbContext()
    {
        var clock = new SystemDateTimeProvider();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_adminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private async Task<List<PasswordResetEmailPayload>> GetPasswordResetEmailPayloadsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        await using var adminDb = BuildAdminDbContext();
        var messages = await adminDb.Set<OutboxMessage>()
            .Where(m => m.Type == OutboxMessageTypes.PasswordResetEmail)
            .ToListAsync();

        return messages
            .Select(m => JsonSerializer.Deserialize<PasswordResetEmailPayload>(encryption.Decrypt(m.EncryptedPayload))!)
            .Where(p => p.UserId == userId)
            .ToList();
    }
}
