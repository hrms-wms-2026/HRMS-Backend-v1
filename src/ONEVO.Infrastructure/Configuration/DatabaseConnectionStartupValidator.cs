using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System.Diagnostics.CodeAnalysis;

namespace ONEVO.Infrastructure.Configuration;

public static class DatabaseConnectionStartupValidator
{
    private const string SetupGuidance =
        "The local .env file is intentionally not committed. Copy .env.example to .env " +
        "and replace the database password placeholders. If the database, roles, or " +
        "migrations are not prepared, run ops/sqlserver/setup-local-db.ps1 -RunMigrations " +
        "from the repository root. After .env exists, dotnet run loads it directly.";

    public static void Validate(
        IConfiguration configuration,
        string environmentName,
        bool processDefaultConnectionOverrideActive = false)
    {
        if (!IsLocalValidationEnvironment(environmentName))
        {
            return;
        }

        var runtimeConnection = ValidateConnection(
            configuration.GetConnectionString("DefaultConnection"),
            "DefaultConnection",
            processDefaultConnectionOverrideActive);
        var migrationConnection = ValidateConnection(
            configuration.GetConnectionString("MigrationConnection"),
            "MigrationConnection",
            processDefaultConnectionOverrideActive);

        var runtimeUsername = runtimeConnection.UserID;
        var migrationUsername = migrationConnection.UserID;

        if (!runtimeUsername.Equals("crownus_app", StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalid(
                "DefaultConnection",
                "the runtime username must be the restricted crownus_app role",
                processDefaultConnectionOverrideActive);
        }

        if (!migrationUsername.Equals("crownus_migrator", StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalid(
                "MigrationConnection",
                "the migration username must be crownus_migrator and must not be sa/admin",
                processDefaultConnectionOverrideActive);
        }

        if (runtimeUsername.Equals(migrationUsername, StringComparison.OrdinalIgnoreCase))
        {
            ThrowInvalid(
                "DefaultConnection/MigrationConnection",
                "runtime and migration connections use the same username",
                processDefaultConnectionOverrideActive);
        }
    }

    public static async Task ValidateAndOpenAsync(
        IConfiguration configuration,
        string environmentName,
        bool processDefaultConnectionOverrideActive = false,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task>? openConnection = null)
    {
        Validate(configuration, environmentName, processDefaultConnectionOverrideActive);
        if (!IsLocalValidationEnvironment(environmentName))
        {
            return;
        }

        var runtimeConnection = configuration.GetConnectionString("DefaultConnection")!;
        try
        {
            if (openConnection is not null)
            {
                await openConnection(runtimeConnection, cancellationToken);
                return;
            }

            await using var connection = new SqlConnection(runtimeConnection);
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var overrideGuidance = processDefaultConnectionOverrideActive
                ? " A process-level ConnectionStrings__* override is active and overrides the repo-root .env."
                : string.Empty;
            throw new InvalidOperationException(
                "The restricted crownus_app DefaultConnection could not authenticate and open. " +
                "Check the local role/password with ops/sqlserver/setup-local-db.ps1 -RunMigrations." +
                overrideGuidance,
                exception);
        }
    }

    private static bool IsLocalValidationEnvironment(string environmentName)
    {
        return environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);
    }

    private static SqlConnectionStringBuilder ValidateConnection(
        string? connectionString,
        string connectionName,
        bool processDefaultConnectionOverrideActive)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ThrowInvalid(connectionName, "it is missing", processDefaultConnectionOverrideActive);
        }

        SqlConnectionStringBuilder parsed;
        try
        {
            parsed = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            ThrowInvalid(
                connectionName,
                "it is not a valid SQL Server connection string",
                processDefaultConnectionOverrideActive);
            throw;
        }

        if (string.IsNullOrWhiteSpace(parsed.UserID))
        {
            ThrowInvalid(connectionName, "the username is missing", processDefaultConnectionOverrideActive);
        }

        if (string.IsNullOrWhiteSpace(parsed.Password))
        {
            ThrowInvalid(connectionName, "the password is missing", processDefaultConnectionOverrideActive);
        }

        if (parsed.Password.Equals("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase)
            || (parsed.Password.StartsWith('<') && parsed.Password.EndsWith('>')))
        {
            ThrowInvalid(
                connectionName,
                "a placeholder password is still configured",
                processDefaultConnectionOverrideActive);
        }

        return parsed;
    }

    [DoesNotReturn]
    private static void ThrowInvalid(
        string connectionName,
        string reason,
        bool processDefaultConnectionOverrideActive = false)
    {
        var overrideGuidance = processDefaultConnectionOverrideActive
            ? " A process-level ConnectionStrings__* override is active and overrides the repo-root .env."
            : string.Empty;
        throw new InvalidOperationException(
            $"Local SQL Server connection '{connectionName}' is invalid because {reason}. " +
            SetupGuidance + overrideGuidance);
    }
}
