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

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Shared, one-time-per-class setup for BaseDomainForgotPasswordIntegrationTests: clones the
/// database and boots the BaseDomainLoginTestFactory once. xUnit's IClassFixture constructs this
/// ONCE and disposes it once after every fact in the class has run, instead of IAsyncLifetime's
/// default of once PER fact - previously this class's own InitializeAsync ran 6 times, once per
/// [Fact]. Every fact seeds its own fresh Guid.NewGuid() tenant/user with a unique slug, and the
/// two facts that used to assert "no token/outbox row exists anywhere" (a pristine-database
/// assumption) now snapshot before/after counts instead, so no cross-fact collision exists from
/// sharing one database across facts.
/// </summary>
public sealed class BaseDomainForgotPasswordIntegrationTestsFixture : IAsyncLifetime
{
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private BaseDomainLoginTestFactory _factory = null!;
    private HttpClient _client = null!;

    public BaseDomainLoginTestFactory Factory => _factory;
    public HttpClient Client => _client;

    public async Task InitializeAsync()
    {
        var connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
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
        await _environmentScope.DisposeAsync();
    }

    public async Task<(int TokenCount, int OutboxCount)> CountTokensAndOutboxMessagesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenCount = await db.PasswordResetTokens.CountAsync();
        var outboxCount = await db.Set<OutboxMessage>()
            .CountAsync(m => m.Type == OutboxMessageTypes.PasswordResetEmail);
        return (tokenCount, outboxCount);
    }

    public static void AssertNoSensitiveFieldsLeaked(string body)
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
    public async Task<List<PasswordResetEmailPayload>> GetPasswordResetEmailPayloadsAsync(
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

    public async Task<(Guid TenantId, Guid UserId, string Email)> SeedActiveUserAsync(string tenantSlug, string email)
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

    public async Task<HttpResponseMessage> PostForgotPasswordAsync(string host, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password");
        request.Headers.Host = host;
        request.Content = JsonContent.Create(new { email });
        return await _client.SendAsync(request);
    }

}

/// <summary>
/// Full-stack proof of forgot-password on both hosts, through real HTTP requests, real
/// HostTenantResolutionMiddleware, and a real PostgreSQL database (including the
/// auth_lookup_base_login_candidates function base-domain forgot-password reuses for its
/// pre-tenant lookup). Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BaseDomainForgotPasswordIntegrationTests : IClassFixture<BaseDomainForgotPasswordIntegrationTestsFixture>
{
    private readonly BaseDomainForgotPasswordIntegrationTestsFixture _fixture;

    public BaseDomainForgotPasswordIntegrationTests(BaseDomainForgotPasswordIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BaseDomain_OneEligibleUser_CreatesOneResetTokenAndDurableEmailWork()
    {
        var (tenantId, userId, email) = await _fixture.SeedActiveUserAsync("fp-base-one", "fp-one@test.onevo.dev");

        var response = await _fixture.PostForgotPasswordAsync(host: "localhost", email);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");
        BaseDomainForgotPasswordIntegrationTestsFixture.AssertNoSensitiveFieldsLeaked(body);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = await db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();

        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantId);
        tokens[0].UsedAt.Should().BeNull();

        var payloads = await _fixture.GetPasswordResetEmailPayloadsAsync(scope, userId);
        payloads.Should().HaveCount(1, "the reset email must be enqueued as a durable outbox job, not fired-and-forgotten");
        payloads[0].TenantId.Should().Be(tenantId);
        payloads[0].Email.Should().Be(email);
        payloads[0].TenantSlug.Should().Be("fp-base-one", "the base-domain reset link must be bound to the tenant that issued it");
    }

    [Fact]
    public async Task BaseDomain_UnknownEmail_ReturnsGenericMessageAndCreatesNoTokenOrEmailWork()
    {
        // Counts are snapshotted before/after (rather than asserting the tables are globally
        // empty) because other facts in this class also create tokens/outbox rows, and under a
        // shared IClassFixture database this fact can run after them.
        var (tokenCountBefore, outboxCountBefore) = await _fixture.CountTokensAndOutboxMessagesAsync();

        var response = await _fixture.PostForgotPasswordAsync(host: "localhost", "nobody-eligible@test.onevo.dev");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");

        var (tokenCountAfter, outboxCountAfter) = await _fixture.CountTokensAndOutboxMessagesAsync();

        tokenCountAfter.Should().Be(tokenCountBefore, "an unknown email must not create any password reset token");
        outboxCountAfter.Should().Be(outboxCountBefore, "an unknown email must not enqueue any reset email work");
    }

    [Fact]
    public async Task BaseDomain_MultipleEligibleTenants_CreatesOneTokenAndOneEmailJobPerTenant_ResponseDisclosesNoWorkspaces()
    {
        const string sharedEmail = "shared-fp@test.onevo.dev";
        var (tenantAId, userAId, _) = await _fixture.SeedActiveUserAsync("fp-multi-a", sharedEmail);
        var (tenantBId, userBId, _) = await _fixture.SeedActiveUserAsync("fp-multi-b", sharedEmail);

        var response = await _fixture.PostForgotPasswordAsync(host: "localhost", sharedEmail);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");
        BaseDomainForgotPasswordIntegrationTestsFixture.AssertNoSensitiveFieldsLeaked(body);
        body.Should().NotContain("fp-multi-a");
        body.Should().NotContain("fp-multi-b");
        body.Should().NotContainEquivalentOf("workspace");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = await db.PasswordResetTokens
            .Where(t => t.UserId == userAId || t.UserId == userBId)
            .ToListAsync();

        tokens.Should().HaveCount(2);
        tokens.Select(t => t.TenantId).Should().BeEquivalentTo(new[] { tenantAId, tenantBId });

        var payloadsA = await _fixture.GetPasswordResetEmailPayloadsAsync(scope, userAId);
        var payloadsB = await _fixture.GetPasswordResetEmailPayloadsAsync(scope, userBId);
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
            await _fixture.SeedActiveUserAsync($"fp-overflow-{i}", sharedEmail);

        // Counts are snapshotted before/after (rather than asserting the tables are globally
        // empty) because other facts in this class also create tokens/outbox rows, and under a
        // shared IClassFixture database this fact can run after them.
        var (tokenCountBefore, outboxCountBefore) = await _fixture.CountTokensAndOutboxMessagesAsync();

        var response = await _fixture.PostForgotPasswordAsync(host: "localhost", sharedEmail);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");
        BaseDomainForgotPasswordIntegrationTestsFixture.AssertNoSensitiveFieldsLeaked(body);

        var (tokenCountAfter, outboxCountAfter) = await _fixture.CountTokensAndOutboxMessagesAsync();

        tokenCountAfter.Should().Be(tokenCountBefore,
            "an email eligible in 9+ tenants must overflow to a full no-op, never partial issuance");
        outboxCountAfter.Should().Be(outboxCountBefore);
    }

    [Fact]
    public async Task TenantHost_ForgotPassword_CreatesTokenAndEmailWorkOnlyForResolvedTenantsUser()
    {
        var (tenantId, userId, email) = await _fixture.SeedActiveUserAsync("fp-tenant-host", "fp-tenant-host@test.onevo.dev");

        var response = await _fixture.PostForgotPasswordAsync(host: "fp-tenant-host.localhost", email);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Contain("If the email exists, a reset link has been sent.");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokens = await db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();

        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantId);

        var payloads = await _fixture.GetPasswordResetEmailPayloadsAsync(scope, userId);
        payloads.Should().HaveCount(1);
        payloads[0].TenantId.Should().Be(tenantId);
        payloads[0].Email.Should().Be(email);
        payloads[0].TenantSlug.Should().Be("fp-tenant-host", "the tenant-host reset link must be tenant-bound too, not resolve to the base host");
    }

    [Fact]
    public async Task TenantHost_ForgotPassword_DoesNotCreateTokenForSameEmailInAnotherTenant()
    {
        const string sharedEmail = "cross-tenant-fp@test.onevo.dev";
        var (tenantAId, userAId, _) = await _fixture.SeedActiveUserAsync("fp-cross-a", sharedEmail);
        var (tenantBId, userBId, _) = await _fixture.SeedActiveUserAsync("fp-cross-b", sharedEmail);

        var response = await _fixture.PostForgotPasswordAsync(host: "fp-cross-a.localhost", sharedEmail);
        response.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenForA = await db.PasswordResetTokens.AnyAsync(t => t.UserId == userAId);
        var tokenForB = await db.PasswordResetTokens.AnyAsync(t => t.UserId == userBId);

        tokenForA.Should().BeTrue("the resolved tenant host's own user must get a token");
        tokenForB.Should().BeFalse("a different tenant's user with the same email must never get a token from another tenant's host");
    }

}
