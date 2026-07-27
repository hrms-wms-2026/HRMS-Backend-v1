using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Full-stack proof of forgot-password on both hosts, through real HTTP requests, real
/// HostTenantResolutionMiddleware, and a real PostgreSQL database (including the
/// auth_lookup_base_login_candidates function base-domain forgot-password reuses for its
/// pre-tenant lookup). Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BaseDomainForgotPasswordIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_forgot_password_integration_test")
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
    public async Task BaseDomain_OneEligibleUser_CreatesOneResetTokenAndDurableEmailWork()
    {
        var (tenantId, userId, email) = await SeedActiveUserAsync("fp-base-one", "fp-one@test.onevo.dev");

        var response = await PostForgotPasswordAsync(host: "localhost", email);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");
        AssertNoSensitiveFieldsLeaked(body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = await db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();

        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantId);
        tokens[0].UsedAt.Should().BeNull();

        var payloads = await GetPasswordResetEmailPayloadsAsync(scope, userId);
        payloads.Should().HaveCount(1, "the reset email must be enqueued as a durable outbox job, not fired-and-forgotten");
        payloads[0].TenantId.Should().Be(tenantId);
        payloads[0].Email.Should().Be(email);
        payloads[0].TenantSlug.Should().Be("fp-base-one", "the base-domain reset link must be bound to the tenant that issued it");
    }

    [Fact]
    public async Task BaseDomain_UnknownEmail_ReturnsGenericMessageAndCreatesNoTokenOrEmailWork()
    {
        var response = await PostForgotPasswordAsync(host: "localhost", "nobody-eligible@test.onevo.dev");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var anyToken = await db.PasswordResetTokens.AnyAsync();
        var anyOutboxMessage = await db.Set<OutboxMessage>()
            .AnyAsync(m => m.Type == OutboxMessageTypes.PasswordResetEmail);

        anyToken.Should().BeFalse();
        anyOutboxMessage.Should().BeFalse();
    }

    [Fact]
    public async Task BaseDomain_MultipleEligibleTenants_CreatesOneTokenAndOneEmailJobPerTenant_ResponseDisclosesNoWorkspaces()
    {
        const string sharedEmail = "shared-fp@test.onevo.dev";
        var (tenantAId, userAId, _) = await SeedActiveUserAsync("fp-multi-a", sharedEmail);
        var (tenantBId, userBId, _) = await SeedActiveUserAsync("fp-multi-b", sharedEmail);

        var response = await PostForgotPasswordAsync(host: "localhost", sharedEmail);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");
        AssertNoSensitiveFieldsLeaked(body);
        body.Should().NotContain("fp-multi-a");
        body.Should().NotContain("fp-multi-b");
        body.Should().NotContainEquivalentOf("workspace");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = await db.PasswordResetTokens
            .Where(t => t.UserId == userAId || t.UserId == userBId)
            .ToListAsync();

        tokens.Should().HaveCount(2);
        tokens.Select(t => t.TenantId).Should().BeEquivalentTo(new[] { tenantAId, tenantBId });

        var payloadsA = await GetPasswordResetEmailPayloadsAsync(scope, userAId);
        var payloadsB = await GetPasswordResetEmailPayloadsAsync(scope, userBId);
        payloadsA.Should().HaveCount(1);
        payloadsB.Should().HaveCount(1);
        payloadsA[0].TenantSlug.Should().Be("fp-multi-a");
        payloadsB[0].TenantSlug.Should().Be("fp-multi-b");
    }

    [Fact]
    public async Task BaseDomain_NineEligibleTenants_TreatsAsOverflow_ReturnsGenericMessageAndCreatesNoTokenOrEmailWork()
    {
        const string sharedEmail = "overflow-fp@test.onevo.dev";
        for (var i = 0; i < 9; i++)
            await SeedActiveUserAsync($"fp-overflow-{i}", sharedEmail);

        var response = await PostForgotPasswordAsync(host: "localhost", sharedEmail);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");
        AssertNoSensitiveFieldsLeaked(body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var anyToken = await db.PasswordResetTokens.AnyAsync();
        var anyOutboxMessage = await db.Set<OutboxMessage>()
            .AnyAsync(m => m.Type == OutboxMessageTypes.PasswordResetEmail);

        anyToken.Should().BeFalse("an email eligible in 9+ tenants must overflow to a full no-op, never partial issuance");
        anyOutboxMessage.Should().BeFalse();
    }

    [Fact]
    public async Task TenantHost_ForgotPassword_CreatesTokenAndEmailWorkOnlyForResolvedTenantsUser()
    {
        var (tenantId, userId, email) = await SeedActiveUserAsync("fp-tenant-host", "fp-tenant-host@test.onevo.dev");

        var response = await PostForgotPasswordAsync(host: "fp-tenant-host.localhost", email);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = await db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();

        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantId);

        var payloads = await GetPasswordResetEmailPayloadsAsync(scope, userId);
        payloads.Should().HaveCount(1);
        payloads[0].TenantId.Should().Be(tenantId);
        payloads[0].Email.Should().Be(email);
        payloads[0].TenantSlug.Should().Be("fp-tenant-host", "the tenant-host reset link must be tenant-bound too, not resolve to the base host");
    }

    [Fact]
    public async Task TenantHost_ForgotPassword_DoesNotCreateTokenForSameEmailInAnotherTenant()
    {
        const string sharedEmail = "cross-tenant-fp@test.onevo.dev";
        var (tenantAId, userAId, _) = await SeedActiveUserAsync("fp-cross-a", sharedEmail);
        var (tenantBId, userBId, _) = await SeedActiveUserAsync("fp-cross-b", sharedEmail);

        var response = await PostForgotPasswordAsync(host: "fp-cross-a.localhost", sharedEmail);
        response.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenForA = await db.PasswordResetTokens.AnyAsync(t => t.UserId == userAId);
        var tokenForB = await db.PasswordResetTokens.AnyAsync(t => t.UserId == userBId);

        tokenForA.Should().BeTrue("the resolved tenant host's own user must get a token");
        tokenForB.Should().BeFalse("a different tenant's user with the same email must never get a token from another tenant's host");
    }

    private static void AssertNoSensitiveFieldsLeaked(string body)
    {
        body.Should().NotContainEquivalentOf("tenant_id");
        body.Should().NotContainEquivalentOf("user_id");
        body.Should().NotContainEquivalentOf("token_hash");
        body.Should().NotContainEquivalentOf("password_hash");
        body.Should().NotContainEquivalentOf("reset_token");
    }

    /// <summary>
    /// Decrypts every pending password_reset_email outbox row for the given user, proving delivery
    /// went through the durable outbox pipeline rather than a fire-and-forget send. Raw reset
    /// tokens live only inside this encrypted-at-rest payload, matching the existing tenant owner
    /// invite outbox's own precedent for one-time tokens.
    /// </summary>
    private async Task<List<PasswordResetEmailPayload>> GetPasswordResetEmailPayloadsAsync(
        IServiceScope scope, Guid userId)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.Type == OutboxMessageTypes.PasswordResetEmail)
            .ToListAsync();

        return messages
            .Select(m => JsonSerializer.Deserialize<PasswordResetEmailPayload>(encryption.Decrypt(m.EncryptedPayload))!)
            .Where(p => p.UserId == userId)
            .ToList();
    }

    private async Task<(Guid TenantId, Guid UserId, string Email)> SeedActiveUserAsync(string tenantSlug, string email)
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPass1!", 12),
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (tenant.Id, user.Id, user.Email);
    }

    private async Task<HttpResponseMessage> PostForgotPasswordAsync(string host, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password");
        request.Headers.Host = host;
        request.Content = JsonContent.Create(new { email });
        return await _client.SendAsync(request);
    }
}
