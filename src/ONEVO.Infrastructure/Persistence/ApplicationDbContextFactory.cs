using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Infrastructure.Persistence;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        LoadDotEnvFile();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Migrations (dotnet ef ...) need DDL rights (CREATE TABLE, FORCE ROW
        // LEVEL SECURITY, CREATE POLICY) that the restricted runtime app role
        // intentionally does not have. Prefer the elevated migration
        // connection. Never fall back to the restricted runtime role.
        var connectionString = configuration.GetConnectionString("MigrationConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'MigrationConnection' not found. Run ops/postgres/setup-local-db.ps1 " +
                "before using dotnet ef.");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        var dateTimeProvider = new SystemDateTimeProvider();
        var currentUser = new AnonymousCurrentUser();

        return new ApplicationDbContext(
            optionsBuilder.Options,
            new AuditableEntityInterceptor(currentUser, dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor()); // System mode — no tenant filter during migrations
    }

    private static void LoadDotEnvFile()
    {
        // Search for repo root like DotEnvLoader does
        var repoRoot = FindRepositoryRoot();
        if (repoRoot == null)
        {
            return;
        }

        var envPath = Path.Combine(repoRoot, ".env");
        if (!File.Exists(envPath))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                continue;
            }

            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmedLine[..separatorIndex].Trim();
            var value = trimmedLine[(separatorIndex + 1)..].Trim();

            // Skip full connection string entries
            if (key.Equals("ConnectionStrings__DefaultConnection", StringComparison.Ordinal) ||
                key.Equals("ConnectionStrings__MigrationConnection", StringComparison.Ordinal))
            {
                continue;
            }

            // Remove quotes if present
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            // Process env wins; do not overwrite an explicit shell/docker value
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        // Build MigrationConnection from atomic values
        BuildConnectionStringIfMissing("ConnectionStrings__MigrationConnection", "ONEVO_DB_MIGRATOR_USER", "ONEVO_DB_MIGRATOR_PASSWORD");
    }

    private static void BuildConnectionStringIfMissing(string connectionKey, string userKey, string passwordKey)
    {
        // Check using both key formats: __ and :
        var key1 = connectionKey; // ConnectionStrings__MigrationConnection
        var key2 = connectionKey.Replace("__", ":"); // ConnectionStrings:MigrationConnection

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key1)) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key2)))
        {
            return;
        }

        var host = Environment.GetEnvironmentVariable("ONEVO_DB_HOST");
        var portText = Environment.GetEnvironmentVariable("ONEVO_DB_PORT");
        var database = Environment.GetEnvironmentVariable("ONEVO_DB_NAME");
        var username = Environment.GetEnvironmentVariable(userKey);
        var password = Environment.GetEnvironmentVariable(passwordKey);

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(portText) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (!int.TryParse(portText, out var port) || port < 1 || port > 65535)
        {
            return;
        }

        var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";
        // Set using both formats to be safe
        Environment.SetEnvironmentVariable(key1, connectionString);
        Environment.SetEnvironmentVariable(key2, connectionString);
    }

    private static string? FindRepositoryRoot(int maxParentDepth = 6)
    {
        var startDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var directory = startDirectory;

        for (var depth = 0; depth <= maxParentDepth && directory != null; depth++, directory = directory.Parent)
        {
            var templatePath = Path.Combine(directory.FullName, ".env.example");
            var apiProjectPath = Path.Combine(directory.FullName, "src", "ONEVO.Api", "ONEVO.Api.csproj");

            if (File.Exists(templatePath) && File.Exists(apiProjectPath))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
