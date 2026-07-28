using System.Text.Json;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class CredentialOwnershipCompletionArchitectureTests
{
    private static readonly string[] ProviderPrefixes =
    [
        "Email__Smtp",
        "Email__Resend__ApiKey",
        "Email__SendGrid__ApiKey",
        "GoogleAuth",
        "Stripe",
        "PayHere"
    ];

    [Fact]
    public void CommittedAppsettings_ContainNoProviderOwnedSectionsOrSecretMarkers()
    {
        var apiDirectory = FindRepositoryPath("src", "ONEVO.Api");
        foreach (var path in Directory.GetFiles(apiDirectory, "appsettings*.json"))
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);

            foreach (var section in new[] { "GoogleAuth", "Stripe", "PayHere", "DevAdmin" })
            {
                Assert.False(
                    document.RootElement.TryGetProperty(section, out _),
                    $"{Path.GetFileName(path)} must not contain the {section} section.");
            }

            Assert.DoesNotContain("CHANGE_ME", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SET_VIA_ENV", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"ApiKey\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"ClientSecret\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"WebhookSecret\"", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EnvironmentTemplate_ContainsNoDirectProviderSettings()
    {
        var keys = ReadEnvironmentKeys(FindRepositoryPath(".env.example"));

        Assert.DoesNotContain(keys, key =>
            ProviderPrefixes.Any(prefix =>
                key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(keys, key => key.Equals("Email__Provider", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Email__FromAddress", keys);
        Assert.Contains("Email__FromName", keys);
    }

    [Fact]
    public void LocalEnvironment_WhenPresent_ContainsNoDeprecatedProviderSettings()
    {
        var path = FindRepositoryPath(".env");
        if (!File.Exists(path))
        {
            return;
        }

        var keys = ReadEnvironmentKeys(path);
        Assert.DoesNotContain(keys, key =>
            ProviderPrefixes.Any(prefix =>
                key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(keys, key => key.Equals("Email__Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StripeWebhook_UsesDatabaseCredentialResolverNotOptions()
    {
        var controller = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Webhooks",
            "StripeWebhookController.cs"));
        var contract = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "DevPlatform",
            "SystemConfig", "PaymentGateway", "ServiceInterfaces",
            "IPaymentGatewayWebhookSecretResolver.cs"));
        var resolver = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Services", "SystemConfig",
            "PaymentGatewayWebhookSecretResolver.cs"));

        Assert.Contains("IPaymentGatewayWebhookSecretResolver", controller, StringComparison.Ordinal);
        Assert.Contains(
            "api/payment-gateways/stripe/{gatewayKey}/webhook",
            controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("api/stripe/webhook", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("StripeOptions", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", controller, StringComparison.Ordinal);
        Assert.Contains("string provider", contract, StringComparison.Ordinal);
        Assert.Contains("string gatewayKey", contract, StringComparison.Ordinal);
        Assert.Contains("GetByGatewayKeyAsync(gatewayKey", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("ListAllAsync", resolver, StringComparison.Ordinal);
        Assert.Contains("WebhookSecretEncrypted", resolver, StringComparison.Ordinal);
        Assert.Contains("DecryptBytes", resolver, StringComparison.Ordinal);
        Assert.Contains("IPaymentGatewayRepository", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentGatewayCreate_EnforcesSharedGatewayKeyRules()
    {
        var handler = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "DevPlatform",
            "SystemConfig", "PaymentGateway", "Commands",
            "CreatePaymentGateway", "CreatePaymentGatewayCommandHandler.cs"));

        Assert.Contains(
            "PaymentGatewayKeyRules.IsValid(request.GatewayKey)",
            handler,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentGatewayMappings_RemainWithinApprovedPhaseOneSchema()
    {
        var configMapping = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "DevPlatform", "SystemConfig", "PaymentGatewayConfigConfiguration.cs"));
        var credentialMapping = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "DevPlatform", "SystemConfig", "PaymentGatewayCredentialConfiguration.cs"));
        var routeMapping = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "DevPlatform", "SystemConfig", "PaymentGatewayCountryRouteConfiguration.cs"));

        Assert.Contains("ToTable(\"payment_gateway_configs\")", configMapping, StringComparison.Ordinal);
        Assert.Contains("HasIndex(g => g.GatewayKey)", configMapping, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", configMapping, StringComparison.Ordinal);

        Assert.Contains("ToTable(\"payment_gateway_credentials\")", credentialMapping, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(credentialMapping, ".HasColumnType(\"bytea\")"));
        Assert.Contains(
            "new { c.PaymentGatewayConfigId, c.IsActive }",
            credentialMapping,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".IsUnique()", credentialMapping, StringComparison.Ordinal);

        Assert.Contains("ToTable(\"payment_gateway_country_routes\")", routeMapping, StringComparison.Ordinal);
        Assert.Contains("r.CountryCode", routeMapping, StringComparison.Ordinal);
        Assert.Contains("r.Environment", routeMapping, StringComparison.Ordinal);
        Assert.Contains("r.IsActive", routeMapping, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsUnique()", routeMapping, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentGatewayRules_StayInApplicationLayerExceptForProviderCatalogForeignKey()
    {
        var rotation = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "DevPlatform",
            "SystemConfig", "PaymentGateway", "Commands",
            "RotatePaymentGatewayCredential",
            "RotatePaymentGatewayCredentialCommandHandler.cs"));
        var create = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "DevPlatform",
            "SystemConfig", "PaymentGateway", "Commands",
            "CreatePaymentGateway", "CreatePaymentGatewayCommandHandler.cs"));

        Assert.Contains("GetActiveCredentialsAsync", rotation, StringComparison.Ordinal);
        Assert.Contains("GetMaxCredentialVersionAsync", rotation, StringComparison.Ordinal);
        Assert.Contains("SaveChangesAsync", rotation, StringComparison.Ordinal);
        Assert.Contains("distinctCountryCodes.Add(code)", create, StringComparison.Ordinal);
        Assert.Contains("HasConflictingCountryRouteAsync", create, StringComparison.Ordinal);

        var migrationsDirectory = FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Migrations");
        var paymentGatewayMigrations = Directory
            .GetFiles(migrationsDirectory, "*.cs")
            .Where(path =>
                !path.EndsWith(".Designer.cs", StringComparison.Ordinal) &&
                !path.EndsWith(
                    "ApplicationDbContextModelSnapshot.cs",
                    StringComparison.Ordinal) &&
                File.ReadAllText(path).Contains(
                    "payment_gateway",
                    StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .ToArray();

        Assert.Equal(
            [
                "20260709122838_AddPaymentGatewaySystemConfigTables.cs",
                "20260724130000_AddPlatformProviderCatalog.cs"
            ],
            paymentGatewayMigrations);

        var providerCatalogMigration = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Migrations",
            "20260724130000_AddPlatformProviderCatalog.cs"));
        Assert.Contains(
            "fk_payment_gateway_configs_platform_providers_provider",
            providerCatalogMigration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CreateTable(\n                name: \"payment_gateway",
            providerCatalogMigration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentGatewaySourceFiles_ContainAsciiOnly()
    {
        var sourceDirectory = FindRepositoryPath("src");
        var paymentGatewayFiles = Directory
            .GetFiles(sourceDirectory, "*PaymentGateway*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));

        foreach (var path in paymentGatewayFiles)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain(
                source,
                character => character > sbyte.MaxValue);
        }
    }

    [Fact]
    public void EfPaymentGatewayRepository_HasNoExpressionBodiedMembers()
    {
        var repositoryPath = FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories",
            "DevPlatform", "SystemConfig", "EfPaymentGatewayRepository.cs");
        var sourceLines = File.ReadAllLines(repositoryPath);

        var expressionBodiedMembers = sourceLines
            .Select((line, index) => new
            {
                LineNumber = index + 1,
                Text = line.Trim()
            })
            .Where(line =>
                line.Text.StartsWith("=>", StringComparison.Ordinal) ||
                (line.Text.StartsWith("public ", StringComparison.Ordinal) &&
                 line.Text.Contains("=>", StringComparison.Ordinal)))
            .Select(line => $"{repositoryPath}:{line.LineNumber}: {line.Text}")
            .ToArray();

        Assert.Empty(expressionBodiedMembers);
    }

    [Fact]
    public void GoogleAuthenticationConsumers_UsePlatformOAuthAppResolverNotOptions()
    {
        foreach (var path in new[]
        {
            FindRepositoryPath(
                "src", "ONEVO.Application", "Features", "Auth", "Login",
                "Commands", "AdminGoogleLogin", "AdminGoogleLoginCommandHandler.cs"),
            FindRepositoryPath(
                "src", "ONEVO.Application", "Features", "Auth", "Invite",
                "Commands", "AcceptInvitationGoogle",
                "AcceptInvitationGoogleCommandHandler.cs")
        })
        {
            var source = File.ReadAllText(path);
            Assert.Contains("IPlatformOAuthAppResolver", source, StringComparison.Ordinal);
            Assert.Contains("GetActiveAppForProviderAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GoogleAuthOptions", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IOptions", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ClientSecret", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DependencyInjection_HasNoDirectProviderOptionsBindings()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "DependencyInjection.cs"));

        Assert.DoesNotContain("Configure<GoogleAuthOptions>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Configure<StripeOptions>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Configure<PayHereOptions>", source, StringComparison.Ordinal);
        Assert.Contains("IPaymentGatewayWebhookSecretResolver", source, StringComparison.Ordinal);
        Assert.Contains("IPlatformOAuthAppResolver", source, StringComparison.Ordinal);
    }

    private static HashSet<string> ReadEnvironmentKeys(string path)
    {
        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith('#') &&
                line.Contains('='))
            .Select(line => line.Split('=', 2)[0].Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int CountOccurrences(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) /
        value.Length;

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "ONEVO.Api")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
