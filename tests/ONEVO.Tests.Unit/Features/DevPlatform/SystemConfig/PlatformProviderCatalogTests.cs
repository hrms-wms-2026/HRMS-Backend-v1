using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class PlatformProviderCatalogTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedProviders =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["google"] = PlatformProviderFamilies.OAuthApp,
            ["github"] = PlatformProviderFamilies.OAuthApp,
            ["microsoft"] = PlatformProviderFamilies.OAuthApp,
            ["zoom"] = PlatformProviderFamilies.OAuthApp,
            ["sendgrid"] = PlatformProviderFamilies.TransactionalEmail,
            ["resend"] = PlatformProviderFamilies.TransactionalEmail,
            ["cloudflare"] = PlatformProviderFamilies.Infrastructure,
            ["cloudflare_r2"] = PlatformProviderFamilies.ObjectStorage,
            ["aws_rekognition"] = PlatformProviderFamilies.AiVerification,
            ["stripe"] = PlatformProviderFamilies.PaymentGateway,
            ["payhere"] = PlatformProviderFamilies.PaymentGateway,
            ["paddle"] = PlatformProviderFamilies.PaymentGateway
        };

    [Fact]
    public void PlatformProviderModel_MatchesCanonicalSevenColumnTableAndSeedsTwelveRows()
    {
        using var db = BuildInMemoryDb();
        var entityType = db.Model.FindEntityType(typeof(PlatformProvider));

        Assert.NotNull(entityType);
        Assert.Equal("platform_providers", entityType!.GetTableName());
        Assert.Equal(
            new[]
            {
                nameof(PlatformProvider.CreatedAt),
                nameof(PlatformProvider.DisplayName),
                nameof(PlatformProvider.Id),
                nameof(PlatformProvider.IsActive),
                nameof(PlatformProvider.ProviderFamily),
                nameof(PlatformProvider.ProviderKey),
                nameof(PlatformProvider.UpdatedAt)
            },
            entityType.GetProperties().Select(property => property.Name).Order().ToArray());

        Assert.Equal(50, entityType.FindProperty(nameof(PlatformProvider.ProviderKey))!.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(PlatformProvider.DisplayName))!.GetMaxLength());
        Assert.Equal(50, entityType.FindProperty(nameof(PlatformProvider.ProviderFamily))!.GetMaxLength());

        var providerKey = entityType.FindProperty(nameof(PlatformProvider.ProviderKey))!;
        Assert.Contains(
            entityType.GetKeys(),
            key => key.Properties.Count == 1 && key.Properties[0] == providerKey);

        var designTimeEntityType = db.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(PlatformProvider))!;
        var seedRows = designTimeEntityType.GetSeedData();
        Assert.Equal(12, seedRows.Count());
        Assert.Equal(
            ExpectedProviders,
            seedRows.ToDictionary(
                row => Assert.IsType<string>(row[nameof(PlatformProvider.ProviderKey)]),
                row => Assert.IsType<string>(row[nameof(PlatformProvider.ProviderFamily)]),
                StringComparer.Ordinal));
        Assert.All(seedRows, row => Assert.True(Assert.IsType<bool>(row[nameof(PlatformProvider.IsActive)])));
    }

    [Fact]
    public void PlatformProviderCatalog_ContainsNoSecretShapedPropertyOrSeedField()
    {
        var forbiddenFragments = new[]
        {
            "secret", "credential", "token", "password", "apikey", "api_key",
            "privatekey", "private_key", "encrypted"
        };

        var propertyNames = typeof(PlatformProvider)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant())
            .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)));

        using var db = BuildInMemoryDb();
        var seedFields = db.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(PlatformProvider))!
            .GetSeedData()
            .SelectMany(row => row.Keys)
            .Select(key => key.ToLowerInvariant())
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(
            seedFields,
            name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)));
    }

    [Fact]
    public void PlatformProviderSurface_IsReadOnlyAndHasNoArbitraryCreateEndpoint()
    {
        Assert.DoesNotContain(
            typeof(IPlatformProviderRepository).GetMethods(),
            method => method.Name.StartsWith("Add", StringComparison.Ordinal) ||
                      method.Name.StartsWith("Create", StringComparison.Ordinal) ||
                      method.Name.StartsWith("Update", StringComparison.Ordinal) ||
                      method.Name.StartsWith("Delete", StringComparison.Ordinal));

        var actions = typeof(PlatformProvidersController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Single(actions);
        Assert.NotNull(actions[0].GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(actions[0].GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(actions[0].GetCustomAttribute<HttpPutAttribute>());
        Assert.Null(actions[0].GetCustomAttribute<HttpPatchAttribute>());
        Assert.Null(actions[0].GetCustomAttribute<HttpDeleteAttribute>());
    }

    [Fact]
    public async Task UnknownProvider_IsRejectedBeforeCredentialEncryptionOrPersistence()
    {
        var serviceKeys = new Mock<IPlatformServiceKeyRepository>();
        var providers = new Mock<IPlatformProviderRepository>();
        providers.Setup(repository => repository.GetActiveFamilyAsync(
                "arbitrary_mailer",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var encryption = new Mock<IEncryptionService>();

        var handler = new CreatePlatformServiceKeyCommandHandler(
            serviceKeys.Object,
            providers.Object,
            encryption.Object);

        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand(
                "arbitrary_mailer",
                "Arbitrary Mailer",
                "plaintext-key",
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        encryption.Verify(
            service => service.Encrypt(It.IsAny<string>()),
            Times.Never);
        serviceKeys.Verify(
            repository => repository.AddAsync(
                It.IsAny<PlatformServiceKey>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SystemConfigProviderCards_ComeFromActivePlatformProviderRows()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.AddRange(
            Provider("resend", "Resend", PlatformProviderFamilies.TransactionalEmail, true),
            Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail, false),
            Provider("google", "Google", PlatformProviderFamilies.OAuthApp, true));
        await db.SaveChangesAsync();

        var repository = new EfPlatformProviderRepository(db);
        var cards = await repository.ListActiveCardsAsync(CancellationToken.None);

        Assert.Equal(new[] { "google", "resend" }, cards.Select(card => card.ProviderKey).Order().ToArray());
        Assert.All(cards, card => Assert.False(card.Configured));
        Assert.DoesNotContain(cards, card => card.ProviderKey == "sendgrid");
    }

    [Fact]
    public async Task TransactionalEmailCredentialQuery_JoinsActiveCatalogFamilyToActiveServiceKeys()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.AddRange(
            Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail, true),
            Provider("resend", "Resend", PlatformProviderFamilies.TransactionalEmail, false),
            Provider("cloudflare", "Cloudflare", PlatformProviderFamilies.Infrastructure, true));
        db.PlatformServiceKeys.AddRange(
            ServiceKey("sendgrid", true),
            ServiceKey("resend", true),
            ServiceKey("cloudflare", true));
        await db.SaveChangesAsync();

        var repository = new EfPlatformServiceKeyRepository(db);
        var matches = await repository.ListActiveForProviderFamilyAsync(
            PlatformProviderFamilies.TransactionalEmail,
            CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.Equal("sendgrid", match.ServiceKey);
    }

    [Fact]
    public void CredentialTablesReferenceProviderCatalogWithoutMovingSecretColumns()
    {
        using var db = BuildInMemoryDb();

        AssertProviderForeignKey<PlatformServiceKey>(
            db,
            nameof(PlatformServiceKey.ServiceKey));
        AssertProviderForeignKey<PlatformOAuthApp>(
            db,
            nameof(PlatformOAuthApp.Provider));
        AssertProviderForeignKey<PaymentGatewayConfig>(
            db,
            nameof(PaymentGatewayConfig.Provider));

        Assert.NotNull(db.Model.FindEntityType(typeof(PlatformServiceKey))!
            .FindProperty(nameof(PlatformServiceKey.ApiKeyEncrypted)));
    }

    [Fact]
    public void ConfigurationFiles_HaveNoRuntimeEmailProviderSelectorOrProviderSecrets()
    {
        var root = FindRepositoryRoot();
        var configFiles = new[]
        {
            Path.Combine(root, ".env.example"),
            Path.Combine(root, "src", "ONEVO.Api", "appsettings.json"),
            Path.Combine(root, "src", "ONEVO.Api", "appsettings.Development.json")
        };

        var text = string.Join(
            Environment.NewLine,
            configFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("Email:Provider", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email__Provider", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENDGRID_API_KEY", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESEND_API_KEY", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SendGrid__ApiKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Resend__ApiKey", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertProviderForeignKey<TEntity>(
        ApplicationDbContext db,
        string propertyName)
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))!;
        Assert.Contains(
            entityType.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(PlatformProvider) &&
                foreignKey.Properties.Count == 1 &&
                foreignKey.Properties[0].Name == propertyName);
    }

    private static PlatformProvider Provider(
        string providerKey,
        string displayName,
        string family,
        bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderKey = providerKey,
            DisplayName = displayName,
            ProviderFamily = family,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PlatformServiceKey ServiceKey(string serviceKey, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            ServiceKey = serviceKey,
            DisplayName = serviceKey,
            ApiKeyEncrypted = $"encrypted-{serviceKey}",
            IsActive = isActive,
            UpdatedById = Guid.NewGuid(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = Mock.Of<IDateTimeProvider>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(Mock.Of<ICurrentUser>(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(Mock.Of<IPublisher>()),
            Mock.Of<ITenantContext>());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".env.example")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
