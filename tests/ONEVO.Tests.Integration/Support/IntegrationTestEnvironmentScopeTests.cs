using FluentAssertions;
using Npgsql;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// IntegrationTestEnvironmentScope is what lets Program.cs's pre-Build() startup validators pass in
/// CI (no repo-root .env, unlike local dev). These tests only exercise process environment
/// variable/connection-string behavior - no Docker/database required. Tagged with the shared
/// WebApplicationFactoryCollection because it mutates the same process-wide environment variable
/// keys every WebApplicationFactory-based integration test relies on.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class IntegrationTestEnvironmentScopeTests
{
    private const string AdminConnectionString =
        "Host=localhost;Port=5432;Database=onevo_env_scope_test;Username=test;Password=test";

    [Fact]
    public void Constructor_SetsAllRequiredProcessEnvironmentVariables()
    {
        using var scope = new IntegrationTestEnvironmentScope(AdminConnectionString);

        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").Should().Be("Test");
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            .Should().Be(scope.DefaultConnectionString);
        Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection")
            .Should().Be(scope.MigrationConnectionString);
        Environment.GetEnvironmentVariable("Encryption__MasterKey")!.Length.Should().BeGreaterThanOrEqualTo(32,
            "ConfigurationStartupValidator requires at least a 32 character master key");
        Environment.GetEnvironmentVariable("Jwt__Secret").Should().NotBeNullOrWhiteSpace();
        Environment.GetEnvironmentVariable("Jwt__TenantIssuer").Should().Be("onevo-api");
        Environment.GetEnvironmentVariable("Jwt__TenantAudience").Should().Be("onevo-api");
        Environment.GetEnvironmentVariable("DevAdmin__Email").Should().Be("test_admin@onevo.dev");
        Environment.GetEnvironmentVariable("DevAdmin__Password").Should().Be("test_password_123");
        Environment.GetEnvironmentVariable("PlatformBootstrap__SuperAdminEmail").Should().Be("test_admin@onevo.dev");
        Environment.GetEnvironmentVariable("PlatformBootstrap__SuperAdminFullName")
            .Should().Be("Integration Test Super Admin");
        Environment.GetEnvironmentVariable("Tenancy__RootDomain").Should().Be("localhost");
    }

    [Fact]
    public void DefaultAndMigrationConnectionStrings_UseTheRestrictedRoleUsernames()
    {
        using var scope = new IntegrationTestEnvironmentScope(AdminConnectionString);

        new NpgsqlConnectionStringBuilder(scope.DefaultConnectionString).Username.Should().Be("onevo_app");
        new NpgsqlConnectionStringBuilder(scope.MigrationConnectionString).Username.Should().Be("onevo_migrator");
    }

    [Fact]
    public void Dispose_RestoresAPreviouslySetEnvironmentVariable()
    {
        const string key = "ConnectionStrings__DefaultConnection";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "sentinel-previous-value");

        try
        {
            var scope = new IntegrationTestEnvironmentScope(AdminConnectionString);
            Environment.GetEnvironmentVariable(key).Should().Be(scope.DefaultConnectionString);

            scope.Dispose();

            Environment.GetEnvironmentVariable(key).Should().Be("sentinel-previous-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public void Dispose_RestoresAPreviouslyUnsetEnvironmentVariableToNull()
    {
        const string key = "Jwt__TenantAudience";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, null);

        try
        {
            var scope = new IntegrationTestEnvironmentScope(AdminConnectionString);
            Environment.GetEnvironmentVariable(key).Should().Be("onevo-api");

            scope.Dispose();

            Environment.GetEnvironmentVariable(key).Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }
}
