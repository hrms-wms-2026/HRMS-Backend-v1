# Forgot-Password Restricted-Role HTTP RLS Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove `POST /api/v1/auth/forgot-password` enforces RLS end-to-end over real HTTP, under the real restricted `onevo_app` runtime role with `TenantRlsInterceptor` wired — closing the gap where existing HTTP tests (`BaseDomainForgotPasswordIntegrationTests`) prove routing but run on a factory that binds `ApplicationDbContext` to the Testcontainers superuser connection and never registers the interceptor, so a 42501 RLS violation could never surface there.

**Architecture:** Add a new `WebApplicationFactory<Program>` (`BaseForgotPasswordRestrictedRoleTestFactory`) that — unlike every existing factory in this suite — does **not** override `ApplicationDbContext`'s registration in `ConfigureServices`. Program.cs's own `ONEVO.Infrastructure.DependencyInjection.AddInfrastructure(builder.Configuration)` already registers `ApplicationDbContext` with `TenantRlsInterceptor` wired, reading `ConnectionStrings:DefaultConnection` from configuration at that point in `Program.cs`'s top-level execution — which runs *before* `WebApplicationFactory.ConfigureWebHost` is ever applied. So as long as `IntegrationTestEnvironmentScope` (already existing) sets the `ConnectionStrings__DefaultConnection` process environment variable to the `onevo_app`-rewritten connection string *before* the factory is constructed, the real production DI wiring does all the work — no hand-wired DI container needed. Test data seeding and verification still go through a separate, directly-constructed admin `ApplicationDbContext` (bypassing RLS on purpose), exactly like `BaseForgotPasswordRlsIntegrationTests` already does.

**Tech Stack:** .NET (C#), xUnit, FluentAssertions, Testcontainers.PostgreSql, Npgsql, EF Core (Npgsql provider), ASP.NET Core `WebApplicationFactory<Program>`.

## Global Constraints

- The new factory's `ConnectionStrings:DefaultConnection` must resolve to the `onevo_app` role, never the Testcontainers superuser connection.
- `ApplicationDbContext` in the new factory must keep `TenantRlsInterceptor` wired — achieved by *not* overriding its registration at all, not by re-adding the interceptor manually.
- EF migrations must run against the admin/superuser connection before the host is ever started (`IntegrationDatabaseBootstrap.InitializeAsync`), exactly like every existing Testcontainers-backed test in this suite.
- Test data (tenants/users) must be seeded via a direct admin-connection `ApplicationDbContext`, bypassing RLS on purpose for setup only.
- The actual forgot-password call under test must go through `AuthPasswordController` via real HTTP (`HttpClient.SendAsync`), not direct handler invocation.
- `BaseForgotPasswordRlsIntegrationTests.cs` must not be deleted or weakened — it remains the narrow, direct-handler RLS proof; the new tests add the full-HTTP-path proof on top.
- No new or existing code may call `SetAdminMode()`, grant/reference `BYPASSRLS`, or contain `DISABLE ROW LEVEL SECURITY` to make any of this pass.

---

### Task 1: Restricted-role `WebApplicationFactory` + its architecture guard

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Auth/BaseForgotPasswordRestrictedRoleTestFactory.cs`
- Create: `tests/ONEVO.Tests.Architecture/ForgotPasswordRestrictedRoleTestFactoryArchitectureTests.cs`

**Interfaces:**
- Produces: `public sealed class BaseForgotPasswordRestrictedRoleTestFactory : WebApplicationFactory<Program>` with constructor `BaseForgotPasswordRestrictedRoleTestFactory(string appConnectionString)`. Task 2 constructs it with `IntegrationTestEnvironmentScope.DefaultConnectionString` (the `onevo_app`-rewritten connection string), after that scope's environment variables are already set.

- [ ] **Step 1: Write the failing architecture guard test**

Create `tests/ONEVO.Tests.Architecture/ForgotPasswordRestrictedRoleTestFactoryArchitectureTests.cs`:

```csharp
using FluentAssertions;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards BaseForgotPasswordRestrictedRoleTestFactory, the WebApplicationFactory
/// BaseForgotPasswordRestrictedRoleHttpIntegrationTests uses to prove
/// POST /api/v1/auth/forgot-password enforces RLS over real HTTP under the restricted onevo_app
/// runtime role. Every other WebApplicationFactory in this suite (BaseDomainLoginTestFactory,
/// E2ETestFactory) strips out AddInfrastructure's ApplicationDbContext registration in
/// ConfigureServices and rebinds it directly to whatever connection string the test passes in -
/// without TenantRlsInterceptor - which makes RLS invisible to any HTTP test built on them (see
/// BaseForgotPasswordRlsIntegrationTests' own doc comment and
/// FORGOT_PASSWORD_DELIVERY_HARDENING_REPORT.md's correction). This factory is deliberately built
/// differently: it must leave Program.cs's own AddInfrastructure(...) wiring completely untouched
/// (real TenantRlsInterceptor, real onevo_app connection string supplied via process environment
/// variables) so the HTTP path is an actual proof of RLS enforcement, not just of routing.
/// </summary>
public sealed class ForgotPasswordRestrictedRoleTestFactoryArchitectureTests
{
    [Fact]
    public void Factory_NeverOverridesApplicationDbContextRegistration()
    {
        var source = ReadSource("BaseForgotPasswordRestrictedRoleTestFactory.cs");

        var forbidden = new[]
        {
            "ConfigureServices",
            "DbContextOptions<ApplicationDbContext>",
            "AddDbContext<ApplicationDbContext>",
            "RemoveAll",
            "services.Remove("
        };

        foreach (var term in forbidden)
        {
            source.Should().NotContain(term,
                $"the factory must leave AddInfrastructure's ApplicationDbContext + TenantRlsInterceptor " +
                $"registration completely untouched, not strip and rebind it like BaseDomainLoginTestFactory/" +
                $"E2ETestFactory do (found forbidden term: {term})");
        }
    }

    [Fact]
    public void Factory_NeverReferencesAdminModeOrRlsDisable()
    {
        var source = ReadSource("BaseForgotPasswordRestrictedRoleTestFactory.cs");

        foreach (var forbidden in new[] { "SetAdminMode", "BYPASSRLS", "DISABLE ROW LEVEL SECURITY" })
        {
            source.Should().NotContain(forbidden,
                $"the restricted-role HTTP factory must never work around RLS via {forbidden}");
        }
    }

    [Fact]
    public void Factory_DocumentsThatCallersMustSupplyTheOnevoAppConnectionString()
    {
        var source = ReadSource("BaseForgotPasswordRestrictedRoleTestFactory.cs");

        source.Should().Contain("onevo_app",
            "the factory's doc comment must make explicit that callers must supply the onevo_app " +
            "connection string (e.g. IntegrationTestEnvironmentScope.DefaultConnectionString), never " +
            "the Testcontainers superuser one");
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tests", "ONEVO.Tests.Integration", "Auth", fileName));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
```

- [ ] **Step 2: Run the architecture tests to verify the guard fails**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --filter "ForgotPasswordRestrictedRoleTestFactoryArchitectureTests" --verbosity minimal`

Expected: FAIL (build error or `FileNotFoundException` — `BaseForgotPasswordRestrictedRoleTestFactory.cs` does not exist yet).

- [ ] **Step 3: Create the restricted-role test factory**

Create `tests/ONEVO.Tests.Integration/Auth/BaseForgotPasswordRestrictedRoleTestFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// WebApplicationFactory for forgot-password HTTP tests that must prove the real onevo_app
/// runtime role + TenantRlsInterceptor combination end to end. Deliberately does NOT override
/// ApplicationDbContext's registration in ConfigureServices - unlike BaseDomainLoginTestFactory
/// and E2ETestFactory (which strip out AddInfrastructure's DbContext registration and rebind
/// ApplicationDbContext directly to whatever connection string the test passes in, without
/// TenantRlsInterceptor - see the identical warning on BaseForgotPasswordRlsIntegrationTests and
/// TenantSessionRlsIntegrationTests), this factory lets Program.cs's own
/// ONEVO.Infrastructure.DependencyInjection.AddInfrastructure(...) wire ApplicationDbContext
/// exactly like production does: the onevo_app connection string, with TenantRlsInterceptor.
///
/// AddInfrastructure reads configuration.GetConnectionString("DefaultConnection") eagerly, as part
/// of Program.cs's top-level statements, before WebApplicationFactory.ConfigureWebHost is ever
/// applied - so the ConfigureAppConfiguration override below only reaches post-Build() config
/// consumers (e.g. the /health/ready postgres check), never AddInfrastructure's own DbContext
/// wiring. The caller MUST therefore set the ConnectionStrings__DefaultConnection process
/// environment variable to the onevo_app connection string (IntegrationTestEnvironmentScope,
/// constructed with the admin connection string, derives this automatically) BEFORE constructing
/// this factory, and pass that same onevo_app connection string into this constructor.
///
/// Requires Docker.
/// </summary>
public sealed class BaseForgotPasswordRestrictedRoleTestFactory : WebApplicationFactory<Program>
{
    private readonly string _appConnectionString;

    public BaseForgotPasswordRestrictedRoleTestFactory(string appConnectionString)
        => _appConnectionString = appConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _appConnectionString,
                ["Jwt:Secret"] = "forgot-password-restricted-role-test-jwt-secret-32c!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["Tenancy:RootDomain"] = "localhost",
                ["Encryption:MasterKey"] = "forgot-password-restricted-role-test-master-key!!",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Forgot Password Restricted Role Test Super Admin"
            });
        });

        // Deliberately no service overrides here. ApplicationDbContext must remain exactly what
        // Program.cs's AddInfrastructure(builder.Configuration) registers in production: the
        // onevo_app connection string (supplied via the process environment variable set before
        // this factory was constructed) with TenantRlsInterceptor wired.
    }
}
```

- [ ] **Step 4: Run the architecture tests to verify they pass**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --filter "ForgotPasswordRestrictedRoleTestFactoryArchitectureTests" --verbosity minimal`

Expected: PASS, 3/3.

- [ ] **Step 5: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Auth/BaseForgotPasswordRestrictedRoleTestFactory.cs tests/ONEVO.Tests.Architecture/ForgotPasswordRestrictedRoleTestFactoryArchitectureTests.cs
git commit -m "test: add restricted-role forgot-password WebApplicationFactory + architecture guard"
```

---

### Task 2: Full-HTTP restricted-role integration tests

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Auth/BaseForgotPasswordRestrictedRoleHttpIntegrationTests.cs`

**Interfaces:**
- Consumes: `BaseForgotPasswordRestrictedRoleTestFactory(string appConnectionString)` (Task 1), `IntegrationTestEnvironmentScope(string adminConnectionString)` with `.DefaultConnectionString` property (existing), `IntegrationDatabaseBootstrap.InitializeAsync(string adminConnectionString)` (existing), `PasswordResetEmailPayload` record with `TenantId`, `UserId`, `Email`, `TenantSlug` properties (existing, `ONEVO.Infrastructure.ExternalServices.Messaging`), `OutboxMessage`/`OutboxMessageTypes.PasswordResetEmail` (existing).

- [ ] **Step 1: Write the three failing HTTP tests**

Create `tests/ONEVO.Tests.Integration/Auth/BaseForgotPasswordRestrictedRoleHttpIntegrationTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail for the right reason if the factory were wrong**

This is a positive-path suite (the underlying RLS fix in `BaseForgotPasswordCommandHandler` already exists and is unit/architecture-tested elsewhere), so there is no pre-existing bug to turn red here. Instead, verify the test actually exercises RLS by temporarily proving it *would* catch a regression:

1. Temporarily comment out the `_tenantSwitcher.SwitchToTenantAsync(...)` call inside `BaseForgotPasswordCommandHandler`'s per-candidate loop (`src/ONEVO.Application/Features/Auth/Login/Commands/BaseForgotPassword/BaseForgotPasswordCommandHandler.cs`).
2. Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "BaseForgotPasswordRestrictedRoleHttpIntegrationTests" --verbosity minimal`
3. Expected: FAIL — the one-eligible-tenant and two-tenant tests return `500` (their `response.StatusCode.Should().Be(HttpStatusCode.OK)` assertion fails), because the handler now tries to insert into RLS-protected `password_reset_tokens` while still in system/root tenant context under the restricted `onevo_app` role. The nine-tenant overflow test still passes (overflow never reaches the tenant switch either way).
4. Revert the temporary comment-out (restore `SwitchToTenantAsync`).

- [ ] **Step 3: Run the tests to verify they pass with the real (unmodified) handler**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "BaseForgotPasswordRestrictedRoleHttpIntegrationTests" --verbosity minimal`

Expected: PASS, 3/3.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Auth/BaseForgotPasswordRestrictedRoleHttpIntegrationTests.cs
git commit -m "test: add full-HTTP restricted-role RLS proof for forgot-password"
```

---

### Task 3: Full verification sweep

**Files:** None (verification only).

- [ ] **Step 1: Build the API project**

Run: `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: `Build succeeded, 0 Warning(s), 0 Error(s)`.

- [ ] **Step 2: Run the targeted unit tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --filter "BaseForgotPassword|RequestPasswordReset|PasswordReset" --verbosity minimal`
Expected: all passed, 0 failed (no change expected here — this task adds no unit tests, only integration + architecture).

- [ ] **Step 3: Run the architecture test suite**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal`
Expected: all passed, including the 3 new `ForgotPasswordRestrictedRoleTestFactoryArchitectureTests`.

- [ ] **Step 4: Run the targeted integration tests**

Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "BaseForgotPasswordRlsIntegrationTests|BaseDomainForgotPasswordIntegrationTests|BaseForgotPasswordRestrictedRoleHttpIntegrationTests" --verbosity minimal`
Expected: all passed — `BaseForgotPasswordRlsIntegrationTests` (3), `BaseDomainForgotPasswordIntegrationTests` (6, unchanged), `BaseForgotPasswordRestrictedRoleHttpIntegrationTests` (3, new).

- [ ] **Step 5: Check for whitespace/conflict-marker errors**

Run: `git diff --check`
Expected: exit 0.

- [ ] **Step 6: Confirm acceptance criteria**

Manually confirm against the task's acceptance list:
- `POST /api/v1/auth/forgot-password` passes under the restricted `onevo_app` runtime DB role over real HTTP (Task 2, all 3 tests green).
- No 42501 occurred (Task 2, Step 2's temporary revert demonstrated the test *would* catch it; Step 3's green run with the real handler confirms it does not occur today).
- Token and outbox rows are created under the correct tenant context (one-tenant and two-tenant tests assert `TenantId`/`TenantSlug` per row).
- Multi-tenant same-email works (two-tenant test).
- Overflow creates no rows (nine-tenant test).
- No RLS weakening/admin workaround was added (Task 1's architecture guard pins this for the new factory; pre-existing `ForgotPasswordHandlers_NeverUseAdminModeOrDisableRls` already pins it for the handlers).

No commit needed for this task (verification only).

## Self-Review Notes

- **Spec coverage:** restricted-role HTTP factory (Task 1), all three requested HTTP test scenarios (Task 2), `BaseForgotPasswordRlsIntegrationTests.cs` left untouched (no task modifies it), architecture guard (Task 1), all five verification commands (Task 3, Steps 1-5).
- **Placeholder scan:** none — every step has literal, compilable code.
- **Type consistency:** `BaseForgotPasswordRestrictedRoleTestFactory(string appConnectionString)` constructor signature matches its Task 2 call site exactly; `PasswordResetEmailPayload`, `OutboxMessage`, `OutboxMessageTypes.PasswordResetEmail`, `Tenant`, `User`, `TenantStatus` all reused as-is from existing sibling files (`BaseForgotPasswordRlsIntegrationTests.cs`, `BaseDomainForgotPasswordIntegrationTests.cs`) with identical namespaces.
