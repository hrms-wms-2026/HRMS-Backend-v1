using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;
using ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset;
using ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Passwords;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// End-to-end proof of the tenant-host password reset flow (forgot-password -> outbox -> raw token
/// -> reset-password) against a real PostgreSQL server under the restricted onevo_app role (NOBYPASSRLS,
/// real TenantRlsInterceptor) - mirrors BaseForgotPasswordRlsIntegrationTests' technique of invoking
/// handlers directly against a production-like DI container rather than mocks. No prior test in this
/// suite exercised ResetPasswordCommandHandler against a real database at all. Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TenantHostPasswordResetFlowIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_reset_flow_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private readonly IEncryptionService _encryption = new AesEncryptionService(
        Options.Create(new EncryptionOptions { MasterKey = "reset-flow-integration-test-master-key-32+chars!!" }));

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
    public async Task ForgotPasswordThenResetPassword_OnTenantHost_SucceedsAndTokenCannotBeReused()
    {
        var (tenantId, slug, userId, email) = await SeedActiveUserAsync("reset-flow-a");

        using var scope = BuildScopeFactory().CreateScope();
        ResolveTenant(scope, tenantId, slug);

        var forgotHandler = scope.ServiceProvider.GetRequiredService<RequestPasswordResetCommandHandler>();
        var forgotResult = await forgotHandler.Handle(new RequestPasswordResetCommand(email), CancellationToken.None);
        forgotResult.IsSuccess.Should().BeTrue();

        var rawToken = await GetLatestRawTokenAsync(userId);
        rawToken.Should().NotBeNullOrEmpty();

        using var resetScope = BuildScopeFactory().CreateScope();
        ResolveTenant(resetScope, tenantId, slug);
        var resetHandler = resetScope.ServiceProvider.GetRequiredService<ResetPasswordCommandHandler>();

        var resetResult = await resetHandler.Handle(
            new ResetPasswordCommand(rawToken!, "BrandNewPassword1"), CancellationToken.None);

        resetResult.IsSuccess.Should().BeTrue();

        await using var verifyDb = BuildAdminDbContext();
        var user = await verifyDb.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var hasher = new BCryptPasswordHasher();
        hasher.Verify("BrandNewPassword1", user.PasswordHash).Should().BeTrue();

        // Same token, second attempt: must fail generically, not succeed again.
        using var replayScope = BuildScopeFactory().CreateScope();
        ResolveTenant(replayScope, tenantId, slug);
        var replayHandler = replayScope.ServiceProvider.GetRequiredService<ResetPasswordCommandHandler>();

        var replayResult = await replayHandler.Handle(
            new ResetPasswordCommand(rawToken!, "AnotherPassword2"), CancellationToken.None);

        replayResult.IsSuccess.Should().BeFalse();
        replayResult.Error.Should().Be("Invalid or expired reset token.");
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_FailsGenerically()
    {
        var (tenantId, slug, userId, email) = await SeedActiveUserAsync("reset-flow-expired");

        using var scope = BuildScopeFactory().CreateScope();
        ResolveTenant(scope, tenantId, slug);
        var forgotHandler = scope.ServiceProvider.GetRequiredService<RequestPasswordResetCommandHandler>();
        await forgotHandler.Handle(new RequestPasswordResetCommand(email), CancellationToken.None);

        var rawToken = await GetLatestRawTokenAsync(userId);
        await ExpireTokenAsync(userId);

        using var resetScope = BuildScopeFactory().CreateScope();
        ResolveTenant(resetScope, tenantId, slug);
        var resetHandler = resetScope.ServiceProvider.GetRequiredService<ResetPasswordCommandHandler>();

        var result = await resetHandler.Handle(
            new ResetPasswordCommand(rawToken!, "BrandNewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid or expired reset token.");
    }

    [Fact]
    public async Task ResetPassword_TokenFromDifferentTenantHost_FailsGenerically()
    {
        var (tenantAId, slugA, userAId, emailA) = await SeedActiveUserAsync("reset-flow-tenant-a");
        var (tenantBId, slugB, _, _) = await SeedActiveUserAsync("reset-flow-tenant-b");

        using var scope = BuildScopeFactory().CreateScope();
        ResolveTenant(scope, tenantAId, slugA);
        var forgotHandler = scope.ServiceProvider.GetRequiredService<RequestPasswordResetCommandHandler>();
        await forgotHandler.Handle(new RequestPasswordResetCommand(emailA), CancellationToken.None);

        var rawToken = await GetLatestRawTokenAsync(userAId);

        // Same raw token, but the request lands on tenant B's host, so tenant B's context is resolved.
        using var resetScope = BuildScopeFactory().CreateScope();
        ResolveTenant(resetScope, tenantBId, slugB);
        var resetHandler = resetScope.ServiceProvider.GetRequiredService<ResetPasswordCommandHandler>();

        var result = await resetHandler.Handle(
            new ResetPasswordCommand(rawToken!, "BrandNewPassword1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid or expired reset token.");
    }

    [Fact]
    public async Task BaseDomainForgotPassword_GeneratedTenantBoundLink_WorksOnTenantHost()
    {
        var (tenantId, slug, userId, email) = await SeedActiveUserAsync("reset-flow-base-domain");

        using var baseScope = BuildScopeFactory().CreateScope();
        // Base-domain requests start unresolved (system mode); BaseForgotPasswordCommandHandler
        // switches into the candidate's own tenant context itself before writing.
        var baseHandler = baseScope.ServiceProvider.GetRequiredService<BaseForgotPasswordCommandHandler>();
        var baseResult = await baseHandler.Handle(new BaseForgotPasswordCommand(email), CancellationToken.None);
        baseResult.IsSuccess.Should().BeTrue();

        var rawToken = await GetLatestRawTokenAsync(userId);
        rawToken.Should().NotBeNullOrEmpty();

        using var resetScope = BuildScopeFactory().CreateScope();
        ResolveTenant(resetScope, tenantId, slug);
        var resetHandler = resetScope.ServiceProvider.GetRequiredService<ResetPasswordCommandHandler>();

        var resetResult = await resetHandler.Handle(
            new ResetPasswordCommand(rawToken!, "BrandNewPassword1"), CancellationToken.None);

        resetResult.IsSuccess.Should().BeTrue();
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddSingleton<IEncryptionService>(_encryption);
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IPermissionVersionService, PermissionVersionService>();
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
        services.AddScoped<EfAuthRepository>();
        services.AddScoped<IUserRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IPasswordResetTokenRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IRefreshTokenRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<IBaseLoginCandidateRepository, EfBaseLoginCandidateRepository>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<RequestPasswordResetCommandHandler>();
        services.AddScoped<ResetPasswordCommandHandler>();
        services.AddScoped<BaseForgotPasswordCommandHandler>();

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static void ResolveTenant(IServiceScope scope, Guid tenantId, string slug)
    {
        var writable = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        writable.Resolve(new TenantRegistryEntry(tenantId, slug, TenantStatus.Active, null));
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

    private async Task<(Guid TenantId, string Slug, Guid UserId, string Email)> SeedActiveUserAsync(string tenantSlug)
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
        var user = new Domain.Features.InfrastructureModule.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = $"{tenantSlug}@test.onevo.dev",
            PasswordHash = new BCryptPasswordHasher().Hash("OriginalPassword1"),
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (tenant.Id, tenant.Slug, user.Id, user.Email);
    }

    private async Task<string?> GetLatestRawTokenAsync(Guid userId)
    {
        await using var db = BuildAdminDbContext();

        var message = await db.Set<OutboxMessage>()
            .Where(m => m.Type == OutboxMessageTypes.PasswordResetEmail)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        foreach (var candidate in message)
        {
            var payload = JsonSerializer.Deserialize<PasswordResetEmailPayload>(
                _encryption.Decrypt(candidate.EncryptedPayload))!;
            if (payload.UserId == userId)
                return payload.RawToken;
        }

        return null;
    }

    private async Task ExpireTokenAsync(Guid userId)
    {
        await using var db = BuildAdminDbContext();
        var token = await db.PasswordResetTokens.SingleAsync(t => t.UserId == userId);
        token.ExpiresAt = _clock.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }
}
