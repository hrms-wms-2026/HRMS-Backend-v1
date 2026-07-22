using Npgsql;
using ONEVO.Api.Configuration;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

[Collection("ProcessEnvironment")]
public sealed class DotEnvLoaderTests
{
    private static readonly string[] ManagedKeys =
    [
        "ONEVO_DB_HOST",
        "ONEVO_DB_PORT",
        "ONEVO_DB_NAME",
        "ONEVO_DB_ADMIN_USER",
        "ONEVO_DB_ADMIN_PASSWORD",
        "ONEVO_DB_APP_USER",
        "ONEVO_DB_APP_PASSWORD",
        "ONEVO_DB_MIGRATOR_USER",
        "ONEVO_DB_MIGRATOR_PASSWORD",
        "ConnectionStrings__DefaultConnection",
        "ConnectionStrings__MigrationConnection",
        "ASPNETCORE_ENVIRONMENT",
        "DOTNET_ENVIRONMENT"
    ];

    [Fact]
    public void LoadIfPresent_BuildsConnectionsFromAtomicValuesAndIgnoresFullConnections()
    {
        var snapshot = CaptureEnvironment();
        var tempDirectory = Directory.CreateTempSubdirectory("onevo-dotenv-");
        var envPath = Path.Combine(tempDirectory.FullName, ".env");
        var output = new StringWriter();
        var originalOutput = Console.Out;
        var errorOutput = new StringWriter();
        var originalErrorOutput = Console.Error;

        try
        {
            ClearManagedEnvironment();
            File.WriteAllLines(envPath,
            [
                "ONEVO_DB_HOST=localhost",
                "ONEVO_DB_PORT=5432",
                "ONEVO_DB_NAME=OnevoDb",
                "ONEVO_DB_ADMIN_USER=postgres",
                "ONEVO_DB_ADMIN_PASSWORD=unit-test-admin-value",
                "ONEVO_DB_APP_USER=onevo_app",
                "ONEVO_DB_APP_PASSWORD=unit-test-app-value",
                "ONEVO_DB_MIGRATOR_USER=onevo_migrator",
                "ONEVO_DB_MIGRATOR_PASSWORD=unit-test-migrator-value",
                "ConnectionStrings__DefaultConnection=Host=ignored;Username=ignored;Password=ignored",
                "ConnectionStrings__MigrationConnection=Host=ignored;Username=ignored;Password=ignored"
            ]);
            Console.SetOut(output);
            Console.SetError(errorOutput);

            DotEnvLoader.LoadIfPresent(envPath, 0);

            var runtime = new NpgsqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
            var migration = new NpgsqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection"));

            Assert.Equal("OnevoDb", Environment.GetEnvironmentVariable("ONEVO_DB_NAME"));
            Assert.Equal("onevo_app", runtime.Username);
            Assert.Equal("unit-test-app-value", runtime.Password);
            Assert.Equal("onevo_migrator", migration.Username);
            Assert.Equal("unit-test-migrator-value", migration.Password);
            Assert.NotEqual("unit-test-admin-value", runtime.Password);
            Assert.NotEqual("unit-test-admin-value", migration.Password);
            Assert.DoesNotContain("unit-test-app-value", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("unit-test-migrator-value", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("unit-test-admin-value", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Ignoring full ConnectionStrings__*", errorOutput.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("unit-test-app-value", errorOutput.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("unit-test-migrator-value", errorOutput.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("unit-test-admin-value", errorOutput.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalErrorOutput);
            RestoreEnvironment(snapshot);
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void LoadIfPresent_DoesNotOverwriteExplicitRuntimeConnection()
    {
        var snapshot = CaptureEnvironment();
        var tempDirectory = Directory.CreateTempSubdirectory("onevo-dotenv-");
        var envPath = Path.Combine(tempDirectory.FullName, ".env");
        const string explicitConnection =
            "Host=explicit;Port=5432;Database=ExplicitDb;Username=explicit_app;Password=unit-test-explicit";

        try
        {
            ClearManagedEnvironment();
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", explicitConnection);
            File.WriteAllLines(envPath,
            [
                "ONEVO_DB_HOST=localhost",
                "ONEVO_DB_PORT=5432",
                "ONEVO_DB_NAME=OnevoDb",
                "ONEVO_DB_APP_USER=onevo_app",
                "ONEVO_DB_APP_PASSWORD=unit-test-app-value",
                "ONEVO_DB_MIGRATOR_USER=onevo_migrator",
                "ONEVO_DB_MIGRATOR_PASSWORD=unit-test-migrator-value"
            ]);

            DotEnvLoader.LoadIfPresent(envPath, 0);

            Assert.Equal(
                explicitConnection,
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
            Assert.True(DotEnvLoader.DefaultConnectionProcessOverrideActive);
        }
        finally
        {
            RestoreEnvironment(snapshot);
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void LoadIfPresent_DoesNotBuildConnectionsFromPasswordPlaceholders()
    {
        var snapshot = CaptureEnvironment();
        var tempDirectory = Directory.CreateTempSubdirectory("onevo-dotenv-");
        var envPath = Path.Combine(tempDirectory.FullName, ".env");

        try
        {
            ClearManagedEnvironment();
            File.WriteAllLines(envPath,
            [
                "ONEVO_DB_HOST=localhost",
                "ONEVO_DB_PORT=5432",
                "ONEVO_DB_NAME=OnevoDb",
                "ONEVO_DB_APP_USER=onevo_app",
                "ONEVO_DB_APP_PASSWORD=<local-app-password>",
                "ONEVO_DB_MIGRATOR_USER=onevo_migrator",
                "ONEVO_DB_MIGRATOR_PASSWORD=<local-migrator-password>"
            ]);

            DotEnvLoader.LoadIfPresent(envPath, 0);

            Assert.Null(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
            Assert.Null(Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection"));
        }
        finally
        {
            RestoreEnvironment(snapshot);
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void LoadIfPresent_PrefersDetectedRepositoryRootOverNestedFile()
    {
        var snapshot = CaptureEnvironment();
        var originalDirectory = Directory.GetCurrentDirectory();
        var tempDirectory = Directory.CreateTempSubdirectory("onevo-dotenv-root-");
        var apiDirectory = Directory.CreateDirectory(
            Path.Combine(tempDirectory.FullName, "src", "ONEVO.Api"));

        try
        {
            ClearManagedEnvironment();
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            WriteRepositoryMarker(tempDirectory.FullName);
            WriteAtomicEnvironment(
                Path.Combine(tempDirectory.FullName, ".env"),
                "root-host");
            WriteAtomicEnvironment(
                Path.Combine(apiDirectory.FullName, ".env"),
                "nested-host");
            Directory.SetCurrentDirectory(apiDirectory.FullName);

            DotEnvLoader.LoadIfPresent();

            var runtime = new NpgsqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
            var migration = new NpgsqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection"));

            Assert.Equal("root-host", runtime.Host);
            Assert.Equal("onevo_app", runtime.Username);
            Assert.Equal("root-host", migration.Host);
            Assert.Equal("onevo_migrator", migration.Username);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            RestoreEnvironment(snapshot);
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void LoadIfPresent_RejectsNestedFileInDevelopment()
    {
        var snapshot = CaptureEnvironment();
        var originalDirectory = Directory.GetCurrentDirectory();
        var tempDirectory = Directory.CreateTempSubdirectory("onevo-dotenv-nested-");
        var apiDirectory = Directory.CreateDirectory(
            Path.Combine(tempDirectory.FullName, "src", "ONEVO.Api"));

        try
        {
            ClearManagedEnvironment();
            WriteRepositoryMarker(tempDirectory.FullName);
            var rootEnvironmentPath = Path.Combine(tempDirectory.FullName, ".env");
            WriteAtomicEnvironment(rootEnvironmentPath, "root-host");
            File.AppendAllLines(rootEnvironmentPath, ["ASPNETCORE_ENVIRONMENT=Development"]);
            WriteAtomicEnvironment(Path.Combine(apiDirectory.FullName, ".env"), "nested-host");
            Directory.SetCurrentDirectory(apiDirectory.FullName);

            var exception = Assert.Throws<InvalidOperationException>(
                () => DotEnvLoader.LoadIfPresent());

            Assert.Contains("repo-root .env", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("delete", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unit-test", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            RestoreEnvironment(snapshot);
            tempDirectory.Delete(true);
        }
    }

    private static void WriteRepositoryMarker(string rootPath)
    {
        File.WriteAllText(Path.Combine(rootPath, ".env.example"), "# test repository marker");
        File.WriteAllText(
            Path.Combine(rootPath, "src", "ONEVO.Api", "ONEVO.Api.csproj"),
            "<Project />");
    }

    private static void WriteAtomicEnvironment(string path, string host)
    {
        File.WriteAllLines(path,
        [
            $"ONEVO_DB_HOST={host}",
            "ONEVO_DB_PORT=5432",
            "ONEVO_DB_NAME=OnevoDb",
            "ONEVO_DB_ADMIN_USER=postgres",
            "ONEVO_DB_ADMIN_PASSWORD=unit-test-admin-value",
            "ONEVO_DB_APP_USER=onevo_app",
            "ONEVO_DB_APP_PASSWORD=unit-test-app-value",
            "ONEVO_DB_MIGRATOR_USER=onevo_migrator",
            "ONEVO_DB_MIGRATOR_PASSWORD=unit-test-migrator-value",
            "ConnectionStrings__DefaultConnection=Host=ignored;Username=ignored;Password=ignored",
            "ConnectionStrings__MigrationConnection=Host=ignored;Username=ignored;Password=ignored"
        ]);
    }

    private static Dictionary<string, string?> CaptureEnvironment()
    {
        var snapshot = new Dictionary<string, string?>();
        foreach (var key in ManagedKeys)
        {
            snapshot[key] = Environment.GetEnvironmentVariable(key);
        }

        return snapshot;
    }

    private static void ClearManagedEnvironment()
    {
        foreach (var key in ManagedKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static void RestoreEnvironment(Dictionary<string, string?> snapshot)
    {
        foreach (var pair in snapshot)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}

[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
