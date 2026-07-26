using Npgsql;

namespace ONEVO.Api.Configuration;

/// <summary>
/// Minimal `.env` loader. When running inside this repository, it loads only
/// the repository-root `.env`; a nested local `.env` can never shadow it.
/// Outside a detectable repository it searches upward and chooses the
/// rootmost candidate within the search limit. Existing process environment
/// variables win, so shell / docker-compose / launchSettings overrides are
/// never clobbered.
///
/// Format:
///   - one KEY=VALUE per line
///   - blank lines and lines starting with '#' are ignored
///   - surrounding single or double quotes around the value are stripped
///   - we deliberately do not support multi-line, escapes, or interpolation;
///     keep your secret values single-line
/// </summary>
public static class DotEnvLoader
{
    public static bool DefaultConnectionProcessOverrideActive { get; private set; }

    public static bool MigrationConnectionProcessOverrideActive { get; private set; }

    public static void LoadIfPresent(string fileName = ".env", int maxParentDepth = 6)
    {
        DefaultConnectionProcessOverrideActive = HasExplicitConnectionOverride(
            "ConnectionStrings__DefaultConnection");
        MigrationConnectionProcessOverrideActive = HasExplicitConnectionOverride(
            "ConnectionStrings__MigrationConnection");

        if (Path.IsPathRooted(fileName))
        {
            if (File.Exists(fileName))
            {
                ApplyFile(fileName);
            }

            BuildDatabaseConnectionStrings();
            return;
        }

        var startDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var repositoryRoot = FindRepositoryRoot(startDirectory, maxParentDepth);
        if (repositoryRoot is not null)
        {
            var rootEnvironmentPath = Path.Combine(repositoryRoot.FullName, fileName);
            RejectNestedEnvironmentFilesInLocalEnvironment(
                startDirectory,
                repositoryRoot,
                rootEnvironmentPath,
                fileName);

            if (File.Exists(rootEnvironmentPath))
            {
                ApplyFile(rootEnvironmentPath);
            }
        }
        else
        {
            var fallbackPath = FindRootmostEnvironmentFile(
                startDirectory,
                fileName,
                maxParentDepth);
            if (fallbackPath is not null)
            {
                ApplyFile(fallbackPath);
            }
        }

        BuildDatabaseConnectionStrings();
    }

    private static DirectoryInfo? FindRepositoryRoot(
        DirectoryInfo startDirectory,
        int maxParentDepth)
    {
        var directory = startDirectory;
        for (var depth = 0;
             depth <= maxParentDepth && directory is not null;
             depth++, directory = directory.Parent)
        {
            var templatePath = Path.Combine(directory.FullName, ".env.example");
            var apiProjectPath = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Api",
                "ONEVO.Api.csproj");
            if (File.Exists(templatePath) && File.Exists(apiProjectPath))
            {
                return directory;
            }
        }

        return null;
    }

    private static string? FindRootmostEnvironmentFile(
        DirectoryInfo startDirectory,
        string fileName,
        int maxParentDepth)
    {
        string? selectedPath = null;
        var directory = startDirectory;
        for (var depth = 0;
             depth <= maxParentDepth && directory is not null;
             depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                selectedPath = candidate;
            }
        }

        return selectedPath;
    }

    private static void RejectNestedEnvironmentFilesInLocalEnvironment(
        DirectoryInfo startDirectory,
        DirectoryInfo repositoryRoot,
        string rootEnvironmentPath,
        string fileName)
    {
        if (!IsLocalEnvironment(rootEnvironmentPath))
        {
            return;
        }

        var nestedPaths = FindNestedEnvironmentFiles(
            startDirectory,
            repositoryRoot,
            fileName);
        if (nestedPaths.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Nested .env files are not allowed in local development or tests. " +
            "Move useful keys to the repo-root .env and delete the nested .env file.");
    }

    private static HashSet<string> FindNestedEnvironmentFiles(
        DirectoryInfo startDirectory,
        DirectoryInfo repositoryRoot,
        string fileName)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = startDirectory;
        while (directory is not null
               && !directory.FullName.Equals(
                   repositoryRoot.FullName,
                   StringComparison.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                paths.Add(Path.GetFullPath(candidate));
            }

            directory = directory.Parent;
        }

        foreach (var scopeName in new[] { "src", "tests", "ops" })
        {
            var scopePath = Path.Combine(repositoryRoot.FullName, scopeName);
            if (!Directory.Exists(scopePath))
            {
                continue;
            }

            foreach (var candidate in Directory.EnumerateFiles(
                         scopePath,
                         fileName,
                         SearchOption.AllDirectories))
            {
                paths.Add(Path.GetFullPath(candidate));
            }
        }

        return paths;
    }

    private static bool IsLocalEnvironment(string rootEnvironmentPath)
    {
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        }

        if (string.IsNullOrWhiteSpace(environmentName) && File.Exists(rootEnvironmentPath))
        {
            environmentName = ReadEnvironmentName(rootEnvironmentPath);
        }

        return environmentName?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true
            || environmentName?.Equals("Test", StringComparison.OrdinalIgnoreCase) == true
            || environmentName?.Equals("Testing", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ReadEnvironmentName(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!key.Equals("DOTNET_ENVIRONMENT", StringComparison.Ordinal)
                && !key.Equals("ASPNETCORE_ENVIRONMENT", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            if ((value.StartsWith('"') && value.EndsWith('"'))
                || (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }

    private static void ApplyFile(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (key.Equals("ConnectionStrings__DefaultConnection", StringComparison.Ordinal)
                || key.Equals("ConnectionStrings__MigrationConnection", StringComparison.Ordinal))
            {
                // Full database connection strings are intentionally ignored.
                // Atomic ONEVO_DB_* values are the single local source and the
                // loader builds both connection strings after reading the file.
                Console.Error.WriteLine(
                    "[CONFIG] Ignoring full ConnectionStrings__* entry from .env. " +
                    "Use atomic ONEVO_DB_* values for local development.");
                continue;
            }

            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            // Process env wins; do not overwrite an explicit shell/docker value.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                continue;

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static void BuildDatabaseConnectionStrings()
    {
        SetConnectionIfMissing(
            "ConnectionStrings__DefaultConnection",
            "ONEVO_DB_APP_USER",
            "ONEVO_DB_APP_PASSWORD");
        SetConnectionIfMissing(
            "ConnectionStrings__MigrationConnection",
            "ONEVO_DB_MIGRATOR_USER",
            "ONEVO_DB_MIGRATOR_PASSWORD");
    }

    private static void SetConnectionIfMissing(
        string connectionEnvironmentKey,
        string usernameEnvironmentKey,
        string passwordEnvironmentKey)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(connectionEnvironmentKey)))
        {
            return;
        }

        var host = Environment.GetEnvironmentVariable("ONEVO_DB_HOST");
        var portText = Environment.GetEnvironmentVariable("ONEVO_DB_PORT");
        var database = Environment.GetEnvironmentVariable("ONEVO_DB_NAME");
        var username = Environment.GetEnvironmentVariable(usernameEnvironmentKey);
        var password = Environment.GetEnvironmentVariable(passwordEnvironmentKey);

        if (IsMissingOrPlaceholder(host)
            || IsMissingOrPlaceholder(portText)
            || IsMissingOrPlaceholder(database)
            || IsMissingOrPlaceholder(username)
            || IsMissingOrPlaceholder(password))
        {
            return;
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            return;
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password
        };

        Environment.SetEnvironmentVariable(
            connectionEnvironmentKey,
            connection.ConnectionString);
    }

    private static bool IsMissingOrPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Equals("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || (value.StartsWith('<') && value.EndsWith('>'));
    }

    private static bool HasExplicitConnectionOverride(string key)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key));
    }
}
