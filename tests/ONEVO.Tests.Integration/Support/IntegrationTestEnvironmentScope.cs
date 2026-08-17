using Npgsql;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Program.cs runs ConfigurationStartupValidator/DatabaseConnectionStartupValidator as top-level
/// statements before WebApplication.CreateBuilder(args) is ever touched by
/// WebApplicationFactory.ConfigureWebHost, so config added inside ConfigureWebHost is too late for
/// them - they read builder.Configuration, which for a fresh process only contains appsettings +
/// process environment variables at that point. Locally this is masked: the repo-root .env loads
/// these same values as process environment variables before Program.cs's validator calls run. CI
/// has no .env, so integration tests must set the same process-level environment variables
/// themselves, against the ephemeral Testcontainers database, before constructing any
/// WebApplicationFactory&lt;Program&gt;.
///
/// Every test class that uses this type must also be tagged
/// [Collection(WebApplicationFactoryCollection.Name)] - process environment variables are global,
/// and xUnit runs distinct test classes in parallel by default, so two tests racing this window
/// could boot a host against the wrong database.
/// </summary>
public sealed class IntegrationTestEnvironmentScope : IDisposable, IAsyncDisposable
{
    private static readonly string[] ManagedKeys =
    {
        "ASPNETCORE_ENVIRONMENT",
        "ConnectionStrings__DefaultConnection",
        "ConnectionStrings__MigrationConnection",
        "Encryption__MasterKey",
        "Jwt__Secret",
        "Jwt__TenantIssuer",
        "Jwt__TenantAudience",
        "DevAdmin__Email",
        "DevAdmin__Password",
        "PlatformBootstrap__SuperAdminEmail",
        "PlatformBootstrap__SuperAdminFullName",
        "Tenancy__RootDomain",
        "AwsRekognition__Region",
        "AwsRekognition__LivenessRoleArn"
    };

    private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);
    private bool _disposed;

    public string DefaultConnectionString { get; }

    public string MigrationConnectionString { get; }

    public IntegrationTestEnvironmentScope(string adminConnectionString)
    {
        DefaultConnectionString = BuildConnectionString(
            adminConnectionString, "onevo_app", PrivilegedRoleTestBootstrap.AppRolePassword);
        MigrationConnectionString = BuildConnectionString(
            adminConnectionString, "onevo_migrator", PrivilegedRoleTestBootstrap.MigratorRolePassword);

        foreach (var key in ManagedKeys)
        {
            _previousValues[key] = Environment.GetEnvironmentVariable(key);
        }

        Set("ASPNETCORE_ENVIRONMENT", "Test");
        Set("ConnectionStrings__DefaultConnection", DefaultConnectionString);
        Set("ConnectionStrings__MigrationConnection", MigrationConnectionString);
        Set("Encryption__MasterKey", "integration-test-env-scope-master-key-32-chars!!");
        Set("Jwt__Secret", "integration-test-env-scope-jwt-secret-32-chars!!");
        Set("Jwt__TenantIssuer", "onevo-api");
        Set("Jwt__TenantAudience", "onevo-api");
        Set("DevAdmin__Email", "test_admin@onevo.dev");
        Set("DevAdmin__Password", "test_password_123");
        Set("PlatformBootstrap__SuperAdminEmail", "test_admin@onevo.dev");
        Set("PlatformBootstrap__SuperAdminFullName", "Integration Test Super Admin");
        Set("Tenancy__RootDomain", "localhost");
        Set("AwsRekognition__Region", "us-east-1");
        Set("AwsRekognition__LivenessRoleArn", "arn:aws:iam::000000000000:role/integration-test-face-liveness");
    }

    private static string BuildConnectionString(string adminConnectionString, string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = username,
            Password = password
        };
        return builder.ConnectionString;
    }

    private static void Set(string key, string value) => Environment.SetEnvironmentVariable(key, value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (key, previousValue) in _previousValues)
        {
            Environment.SetEnvironmentVariable(key, previousValue);
        }

        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
