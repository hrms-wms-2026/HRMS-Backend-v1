using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Proves TenantDatabaseTicketStore.StoreAsync can insert into "sessions" while the ApplicationDbContext
/// connects as the real onevo_app role (NOBYPASSRLS, subject to the tenant_isolation RLS policy) with
/// TenantRlsInterceptor wired. This is deliberately NOT built on BaseDomainLoginTestFactory/E2ETestFactory:
/// both of those bind ApplicationDbContext to the Testcontainers superuser connection and omit
/// AddInterceptors(TenantRlsInterceptor), so RLS is invisible there - a session insert succeeds or fails
/// identically whether or not the tenant context bug in StoreAsync is fixed. This class is the one place
/// in the suite that actually exercises RLS for session writes. Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TenantSessionRlsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_ticket_store_rls_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _adminConnectionString = null!;
    private string _appConnectionString = null!;
    private IntegrationTestEnvironmentScope _environmentScope = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _adminConnectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(_adminConnectionString);

        // Also sets/restores process env vars for the shared WebApplicationFactoryCollection; harmless
        // here since nothing in this class boots a WebApplicationFactory, but DefaultConnectionString is
        // exactly the onevo_app-authenticated connection string this test needs.
        _environmentScope = new IntegrationTestEnvironmentScope(_adminConnectionString);
        _appConnectionString = _environmentScope.DefaultConnectionString;

        await GrantOnevoAppTablePrivilegesAsync();
    }

    /// <summary>
    /// IntegrationDatabaseBootstrap runs EF migrations over the Testcontainers superuser connection
    /// (never onevo_migrator), so the production ALTER DEFAULT PRIVILEGES step in
    /// ops/postgres/local-bootstrap-roles.sql never fires here and onevo_app ends up with no grants at
    /// all on the tables migrations created. This reproduces only the blanket fallback grant from that
    /// same script (lines 67-75) - onevo_app remains NOBYPASSRLS; this is an object-level ACL grant, not
    /// an RLS change, and mirrors what production already grants onevo_app via default privileges.
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

    public async Task DisposeAsync()
    {
        await _environmentScope.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task StoreAsync_UnderRealOnevoAppRoleWithRlsInterceptor_InsertsSessionWithoutRlsViolation()
    {
        var (tenantId, userId) = await SeedActiveTenantAndUserAsync();

        var scopeFactory = BuildProductionLikeScopeFactory();
        var sut = new TenantDatabaseTicketStore(scopeFactory, _clock);
        var ticket = BuildTicket(tenantId, userId);

        var act = () => sut.StoreAsync(ticket, httpContext: null, CancellationToken.None);

        (await act.Should().NotThrowAsync(
            "the fixed StoreAsync must switch this scope's DbContext into the correct tenant context " +
            "before inserting into the RLS-protected sessions table")).Which.Should().NotBeNullOrEmpty();

        await using var verifyDb = BuildAdminDbContext();
        var session = await verifyDb.Sessions.SingleOrDefaultAsync(s => s.UserId == userId);
        session.Should().NotBeNull();
        session!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task StoreAsync_UnderRealOnevoAppRole_TenantInactive_DoesNotInsertSession_NoRlsBypass()
    {
        var (tenantId, userId) = await SeedActiveTenantAndUserAsync();
        await SetTenantStatusAsync(tenantId, TenantStatus.Cancelled);

        var scopeFactory = BuildProductionLikeScopeFactory();
        var sut = new TenantDatabaseTicketStore(scopeFactory, _clock);
        var ticket = BuildTicket(tenantId, userId);

        var act = () => sut.StoreAsync(ticket, httpContext: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verifyDb = BuildAdminDbContext();
        (await verifyDb.Sessions.AnyAsync(s => s.UserId == userId)).Should().BeFalse(
            "an inactive tenant must never result in a session row, regardless of RLS");
    }

    private static AuthenticationTicket BuildTicket(Guid tenantId, Guid userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));
        var properties = new AuthenticationProperties();
        properties.Items[TenantDatabaseTicketStore.TenantIdItemKey] = tenantId.ToString();
        return new AuthenticationTicket(principal, properties, "TenantScheme");
    }

    /// <summary>
    /// Mirrors ONEVO.Infrastructure.DependencyInjection's registration of the exact services
    /// TenantDatabaseTicketStore resolves per-scope, pointed at the onevo_app connection string instead
    /// of the admin one - i.e. what the running API actually wires up in production.
    /// </summary>
    private IServiceScopeFactory BuildProductionLikeScopeFactory()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider>(_clock);
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
        services.AddScoped<EfAuthRepository>();
        services.AddScoped<ISessionRepository>(sp => sp.GetRequiredService<EfAuthRepository>());
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();

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

    private async Task<(Guid TenantId, Guid UserId)> SeedActiveTenantAndUserAsync()
    {
        await using var db = BuildAdminDbContext();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "rls-fix-tenant",
            Slug = "rls-fix-tenant",
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = "rls-fix@test.onevo.dev",
            PasswordHash = "irrelevant-hash",
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (tenant.Id, user.Id);
    }

    private async Task SetTenantStatusAsync(Guid tenantId, TenantStatus status)
    {
        await using var db = BuildAdminDbContext();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        tenant.Status = status;
        await db.SaveChangesAsync();
    }
}
