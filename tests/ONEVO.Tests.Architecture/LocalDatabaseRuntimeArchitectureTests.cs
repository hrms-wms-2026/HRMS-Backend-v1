using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class LocalDatabaseRuntimeArchitectureTests
{
    [Fact]
    public void HostedSeeders_DoNotRunEfMigrations()
    {
        var seedersDirectory = FindRepositoryPath("src", "ONEVO.Infrastructure", "Persistence", "Seeders");
        var offenders = Directory.GetFiles(seedersDirectory, "*.cs")
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("MigrateAsync(", StringComparison.Ordinal)
                    || source.Contains("EnsureCreated(", StringComparison.Ordinal)
                    || source.Contains("EnsureCreatedAsync(", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DesignTimeFactory_DoesNotFallBackToRuntimeConnection()
    {
        var factoryPath = FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "ApplicationDbContextFactory.cs");
        var source = File.ReadAllText(factoryPath);

        Assert.DoesNotContain("?? configuration.GetConnectionString(\"DefaultConnection\")", source, StringComparison.Ordinal);
        Assert.Contains("GetConnectionString(\"MigrationConnection\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSetupFiles_DoNotContainCommittedDatabasePasswordsOrFullEnvConnectionStrings()
    {
        var envTemplate = File.ReadAllText(FindRepositoryPath(".env.example"));
        var setupScript = File.ReadAllText(FindRepositoryPath("ops", "postgres", "setup-local-db.ps1"));
        var bootstrapSql = File.ReadAllText(FindRepositoryPath("ops", "postgres", "local-bootstrap-roles.sql"));

        Assert.DoesNotMatch(new Regex("^ConnectionStrings__.*=", RegexOptions.Multiline), envTemplate);
        Assert.DoesNotContain("onevo_app_dev_only", setupScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onevo_migrator_dev_only", setupScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onevo_app_dev_only", bootstrapSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onevo_migrator_dev_only", bootstrapSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ONEVO_DB_ADMIN_PASSWORD=<local-postgres-admin-password>", envTemplate, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_APP_PASSWORD=<local-runtime-password>", envTemplate, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_MIGRATOR_PASSWORD=<local-migration-password>", envTemplate, StringComparison.Ordinal);
        Assert.Contains("Encryption__MasterKey=<local-32-plus-character-random-secret>", envTemplate, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_ADMIN_PASSWORD", setupScript, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_APP_PASSWORD", setupScript, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_MIGRATOR_PASSWORD", setupScript, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__MigrationConnection", setupScript, StringComparison.Ordinal);
        Assert.Contains("$previousPgPassword", setupScript, StringComparison.Ordinal);
        Assert.Contains("finally", setupScript, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex("Write-(Host|Output).*Password", RegexOptions.IgnoreCase),
            setupScript);

        var envBytes = File.ReadAllBytes(FindRepositoryPath(".env.example"));
        Assert.All(envBytes, value => Assert.InRange(value, (byte)0, (byte)127));
    }

    [Fact]
    public void LocalSetupScript_ExecutesCreateDatabaseBlockFromTemporarySqlFile()
    {
        var setupScript = File.ReadAllText(
            FindRepositoryPath("ops", "postgres", "setup-local-db.ps1"));
        var argumentsStart = setupScript.IndexOf("$createDatabaseArguments", StringComparison.Ordinal);
        var invocationStart = setupScript.IndexOf(
            "Invoke-PsqlChecked -Executable $psqlExecutable -Arguments $createDatabaseArguments",
            argumentsStart,
            StringComparison.Ordinal);
        var createDatabaseArgumentsBlock = setupScript[argumentsStart..invocationStart];

        Assert.DoesNotContain("'--command'", createDatabaseArgumentsBlock, StringComparison.Ordinal);
        Assert.Contains("'--set', \"db_name=$databaseName\"", setupScript, StringComparison.Ordinal);
        Assert.Contains("'--file', $createDatabaseSqlPath", setupScript, StringComparison.Ordinal);
        Assert.Contains(
            "Remove-Item -LiteralPath $createDatabaseSqlPath",
            setupScript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentTemplate_ContainsOnlyEmptyOrPlaceholderSecretValues()
    {
        var templateLines = File.ReadAllLines(FindRepositoryPath(".env.example"));
        var secretValues = templateLines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && IsSecretSetting(parts[0]))
            .Select(parts => new { Key = parts[0], Value = parts[1].Trim() })
            .ToList();

        Assert.NotEmpty(secretValues);
        Assert.All(secretValues, setting =>
        {
            var isSafePlaceholder = setting.Value.Length == 0
                || Regex.IsMatch(setting.Value, "^<[^>]+>$", RegexOptions.CultureInvariant);
            Assert.True(isSafePlaceholder, $"{setting.Key} contains a non-placeholder value.");
        });
    }

    [Fact]
    public void AppsettingsFiles_ContainNoSecretOwnedSettingsOrSecretShapedValues()
    {
        var apiDirectory = FindRepositoryPath("src", "ONEVO.Api");
        foreach (var path in Directory.GetFiles(apiDirectory, "appsettings*.json"))
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var settings = FlattenJson(document.RootElement).ToList();

            foreach (var marker in new[] { "CHANGE_ME", "SET_VIA_ENV" })
            {
                Assert.DoesNotContain(marker, text, StringComparison.OrdinalIgnoreCase);
            }

            var forbiddenPaths = new[]
            {
                "DevAdmin:Password",
                "Jwt:Secret",
                "Jwt:AdminSecret",
                "Encryption:MasterKey",
                "GoogleAuth:Admin:ClientSecret",
                "GoogleAuth:TenantInvite:ClientSecret",
                "Stripe:SecretKey",
                "Stripe:WebhookSecret",
                "PayHere:MerchantSecret",
                "PayHere:WebhookSecret",
                "Email:Smtp:Password",
                "Email:Resend:ApiKey",
                "Email:SendGrid:ApiKey"
            };
            Assert.DoesNotContain(settings, setting =>
                forbiddenPaths.Contains(setting.Path, StringComparer.OrdinalIgnoreCase));

            Assert.DoesNotMatch(
                new Regex(@"Password\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                text);

            var realSecretPattern = new Regex(
                @"^(sk_(live|test)_|rk_(live|test)_|whsec_|SG\.|re_)[A-Za-z0-9_\-.]{8,}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.DoesNotContain(settings, setting => realSecretPattern.IsMatch(setting.Value));
        }
    }

    [Fact]
    public void EnvironmentTemplate_ContainsOnlyApprovedEmailRuntimeSettings()
    {
        var lines = File.ReadAllLines(FindRepositoryPath(".env.example"));
        var keys = lines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Split('=', 2)[0].Trim())
            .ToList();

        var emailKeys = keys
            .Where(key => key.StartsWith("Email__", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(
            new[] { "Email__FromAddress", "Email__FromName" },
            emailKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray());

        foreach (var forbidden in new[]
        {
            "Email__Provider",
            "Email__Smtp__Password",
            "Email__Resend__ApiKey",
            "Email__SendGrid__ApiKey"
        })
        {
            Assert.DoesNotContain(forbidden, keys, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PlatformAccessSeeder_HashesBootstrapPasswordAndDoesNotLogIt()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Seeders", "PlatformAccessSeeder.cs"));

        Assert.Contains("PasswordHash = passwordHasher.Hash(bootstrapPassword)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash = bootstrapPassword", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\([^;]*bootstrapPassword",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void TransactionalEmail_ResolvesApiKeysOnlyFromPlatformServiceKeys()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "ExternalServices", "Email",
            "PlatformKeyTransactionalEmailSender.cs"));

        Assert.Contains("IPlatformServiceKeyResolver", source, StringComparison.Ordinal);
        Assert.Contains("ResolveActiveTransactionalEmailProviderAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Email:Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email__Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email:Resend:ApiKey", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email:SendGrid:ApiKey", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email:Smtp:Password", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IConfiguration", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_ContainsNoNestedEnvironmentFilesAndKeepsTemplateSafe()
    {
        var root = FindRepositoryPath();
        var nestedEnvironmentFiles = new List<string>();
        foreach (var directoryName in new[] { "src", "tests", "ops" })
        {
            var directory = Path.Combine(root, directoryName);
            nestedEnvironmentFiles.AddRange(
                Directory.GetFiles(directory, ".env", SearchOption.AllDirectories));
        }

        Assert.Empty(nestedEnvironmentFiles);

        var gitignore = File.ReadAllLines(Path.Combine(root, ".gitignore"));
        Assert.Contains(".env", gitignore);
        Assert.Contains(".env.*.local", gitignore);
        Assert.Contains("**/.env", gitignore);
        Assert.DoesNotContain(".env.example", gitignore);

        var template = File.ReadAllText(Path.Combine(root, ".env.example"));
        Assert.Contains("ONEVO_DB_ADMIN_PASSWORD=<local-postgres-admin-password>", template, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_APP_PASSWORD=<local-runtime-password>", template, StringComparison.Ordinal);
        Assert.Contains("ONEVO_DB_MIGRATOR_PASSWORD=<local-migration-password>", template, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("^ConnectionStrings__.*=", RegexOptions.Multiline), template);

        var templateBytes = File.ReadAllBytes(Path.Combine(root, ".env.example"));
        Assert.All(templateBytes, value => Assert.InRange(value, (byte)0, (byte)127));
    }

    [Fact]
    public void ProductionSource_DoesNotReferenceLocalAdminPassword()
    {
        var sourceRoot = FindRepositoryPath("src");
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "ONEVO_DB_ADMIN_PASSWORD",
                StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Program_ValidatesRequiredInfrastructureBeforeRegisteringHostedServices()
    {
        var program = File.ReadAllText(FindRepositoryPath("src", "ONEVO.Api", "Program.cs"));
        var encryptionValidation = program.IndexOf(
            "ValidateRequiredLocalConfiguration(",
            StringComparison.Ordinal);
        var databaseValidation = program.IndexOf("ValidateAndOpenAsync(", StringComparison.Ordinal);
        var infrastructureRegistration = program.IndexOf("AddInfrastructure(", StringComparison.Ordinal);

        Assert.True(encryptionValidation >= 0);
        Assert.True(databaseValidation > encryptionValidation);
        Assert.True(infrastructureRegistration > databaseValidation);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static bool IsSecretSetting(string key)
    {
        return key.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
            || key.Contains("MasterKey", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Path, string Value)> FlattenJson(
        JsonElement element,
        string parentPath = "")
    {
        foreach (var property in element.EnumerateObject())
        {
            var path = string.IsNullOrEmpty(parentPath)
                ? property.Name
                : $"{parentPath}:{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in FlattenJson(property.Value, path))
                {
                    yield return child;
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                yield return (path, property.Value.GetString() ?? string.Empty);
            }
        }
    }
}
