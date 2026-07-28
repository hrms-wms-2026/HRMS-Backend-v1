using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Proves BaseForgotPasswordCommandHandler can insert into password_reset_tokens (RLS-protected,
/// tenant-owned) while ApplicationDbContext connects as the real onevo_app role (NOBYPASSRLS,
/// subject to the tenant_isolation policy) with TenantRlsInterceptor wired. This is deliberately
/// NOT built on BaseDomainLoginTestFactory, the factory BaseDomainForgotPasswordIntegrationTests
/// uses: that factory binds ApplicationDbContext straight to the Testcontainers superuser
/// connection and never registers TenantRlsInterceptor (see the identical warning on
/// TenantSessionRlsIntegrationTests, the sibling test class this one mirrors), so RLS is invisible
/// there - a password_reset_tokens insert succeeds or fails identically whether or not this
/// handler ever switches tenant context. Confirmed empirically while fixing the underlying 42501
/// bug: temporarily reverting BaseForgotPasswordCommandHandler's SwitchToTenantAsync call still
/// left every BaseDomainForgotPasswordIntegrationTests test green. This class is the one place in
/// the suite that actually exercises RLS for base-domain forgot-password writes, invoking the
/// handler directly (bypassing HTTP/WebApplicationFactory) against a hand-wired, production-like
/// DI container - the same technique TenantSessionRlsIntegrationTests uses for
/// TenantDatabaseTicketStore. Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BaseForgotPasswordRlsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_forgot_password_rls_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private readonly IEncryptionService _encryption = new AesEncryptionService(
        Options.Create(new EncryptionOptions { MasterKey = "forgot-password-rls-test-master-key-32+chars!!" }));

    private string _adminConnectionString = null!;
    private string _appConnectionString = null!;
    private IntegrationTestEnvironmentScope _environmentScope = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _adminConnectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(_adminConnectionString);

        _environmentScope = new IntegrationTestEnvironmentScope(_adminConnectionString);
        _appConnectionString = _environmentScope.DefaultConnectionString;

        await GrantOnevoAppTablePrivilegesAsync();
    }

    public async Task DisposeAsync()
    {
        await _environmentScope.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Handle_OneEligibleCandidate_UnderRealOnevoAppRoleWithRlsInterceptor_CreatesTokenAndOutboxRowWithoutRlsViolation()
    {
        var (tenantId, userId, email) = await SeedActiveUserAsync("rls-fp-one", "rls-fp-one@test.onevo.dev");

        using var scope = BuildProductionLikeScopeFactory().CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BaseForgotPasswordCommandHandler>();

        var act = () => handler.Handle(new BaseForgotPasswordCommand(email), CancellationToken.None);

        var result = await act.Should().NotThrowAsync(
            "the fixed handler must switch into the candidate's tenant context before inserting into " +
            "the RLS-protected password_reset_tokens table, not fail with 42501");
        result.Which.IsSuccess.Should().BeTrue();

        await using var verifyDb = BuildAdminDbContext();
        var tokens = await verifyDb.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();
        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantId);

        var payloads = await GetPasswordResetEmailPayloadsAsync(userId);
        payloads.Should().HaveCount(1);
        payloads[0].TenantId.Should().Be(tenantId);
        payloads[0].TenantSlug.Should().Be("rls-fp-one");
    }

    [Fact]
    public async Task Handle_MultipleEligibleTenants_UnderRealOnevoAppRoleWithRlsInterceptor_CreatesOneTokenPerTenantWithoutCrossTenantRlsViolation()
    {
        const string sharedEmail = "rls-fp-shared@test.onevo.dev";
        var (tenantAId, userAId, _) = await SeedActiveUserAsync("rls-fp-a", sharedEmail);
        var (tenantBId, userBId, _) = await SeedActiveUserAsync("rls-fp-b", sharedEmail);

        using var scope = BuildProductionLikeScopeFactory().CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BaseForgotPasswordCommandHandler>();

        var act = () => handler.Handle(new BaseForgotPasswordCommand(sharedEmail), CancellationToken.None);

        var result = await act.Should().NotThrowAsync(
            "each candidate's tenant switch + write + save must be independent, so tenant B's insert " +
            "must not be blocked by tenant A's now-active session context, or vice versa");
        result.Which.IsSuccess.Should().BeTrue();

        await using var verifyDb = BuildAdminDbContext();
        var tokens = await verifyDb.PasswordResetTokens
            .Where(t => t.UserId == userAId || t.UserId == userBId)
            .ToListAsync();
        tokens.Should().HaveCount(2);
        tokens.Select(t => t.TenantId).Should().BeEquivalentTo(new[] { tenantAId, tenantBId });

        var payloadsA = await GetPasswordResetEmailPayloadsAsync(userAId);
        var payloadsB = await GetPasswordResetEmailPayloadsAsync(userBId);
        payloadsA.Should().HaveCount(1);
        payloadsB.Should().HaveCount(1);
        payloadsA[0].TenantSlug.Should().Be("rls-fp-a");
        payloadsB[0].TenantSlug.Should().Be("rls-fp-b");
    }

    [Fact]
    public async Task Handle_NineCandidates_UnderRealOnevoAppRoleWithRlsInterceptor_OverflowNeverSwitchesTenantOrWrites()
    {
        const string sharedEmail = "rls-fp-overflow@test.onevo.dev";
        for (var i = 0; i < 9; i++)
            await SeedActiveUserAsync($"rls-fp-overflow-{i}", sharedEmail);

        using var scope = BuildProductionLikeScopeFactory().CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BaseForgotPasswordCommandHandler>();

        var result = await handler.Handle(new BaseForgotPasswordCommand(sharedEmail), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verifyDb = BuildAdminDbContext();
        (await verifyDb.PasswordResetTokens.AnyAsync()).Should().BeFalse(
            "overflow must never touch any candidate's tenant context or create a token");
        (await verifyDb.Set<OutboxMessage>().AnyAsync(m => m.Type == OutboxMessageTypes.PasswordResetEmail))
            .Should().BeFalse();
    }

    /// <summary>
    /// Mirrors ONEVO.Infrastructure.DependencyInjection's registration of exactly the services
    /// BaseForgotPasswordCommandHandler resolves, pointed at the onevo_app connection string
    /// instead of the admin one - i.e. what the running API actually wires up in production.
    /// </summary>
    private IServiceScopeFactory BuildProductionLikeScopeFactory()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddSingleton<IEncryptionService>(_encryption);
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<ILogger<BaseForgotPasswordCommandHandler>>(
            NullLogger<BaseForgotPasswordCommandHandler>.Instance);

        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped(_ => new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock));
        services.AddScoped(_ => new SoftDeleteInterceptor(_clock));
        services.AddScoped(_ => new DomainEventDispatchInterceptor(new NoOpPublisher()));

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(_appConnectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IBaseLoginCandidateRepository, EfBaseLoginCandidateRepository>();
        services.AddScoped<EfAuthRepository>();
        services.AddScoped<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<BaseForgotPasswordCommandHandler>();

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private ApplicationDbContext BuildAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_adminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    /// <summary>
    /// IntegrationDatabaseBootstrap runs EF migrations over the Testcontainers superuser connection
    /// (never onevo_migrator), so the production ALTER DEFAULT PRIVILEGES step in
    /// ops/postgres/local-bootstrap-roles.sql never fires here and onevo_app ends up with no grants
    /// at all on the tables migrations created. This reproduces only the blanket fallback grant
    /// from that same script - onevo_app remains NOBYPASSRLS; this is an object-level ACL grant,
    /// not an RLS change, and mirrors what production already grants onevo_app via default
    /// privileges. Identical to TenantSessionRlsIntegrationTests.GrantOnevoAppTablePrivilegesAsync.
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

    private async Task<List<PasswordResetEmailPayload>> GetPasswordResetEmailPayloadsAsync(Guid userId)
    {
        await using var db = BuildAdminDbContext();

        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.Type == OutboxMessageTypes.PasswordResetEmail)
            .ToListAsync();

        return messages
            .Select(m => JsonSerializer.Deserialize<PasswordResetEmailPayload>(_encryption.Decrypt(m.EncryptedPayload))!)
            .Where(p => p.UserId == userId)
            .ToList();
    }
}
