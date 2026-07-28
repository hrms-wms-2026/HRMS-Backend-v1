using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Moq;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ConfigurePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.SetPlatformOAuthAppActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ValidatePlatformOAuthAppConfig;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.GetPlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.ListPlatformOAuthApps;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.SystemConfig;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public class PlatformOAuthAppsTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static PlatformOAuthApp ExistingApp(
        string provider = "github", bool isActive = true, string clientId = "Iv1.abc123") => new()
    {
        Id = Guid.NewGuid(),
        Provider = provider,
        AppName = "ONEVO for GitHub",
        LogoUrl = null,
        ClientId = clientId,
        AuthorizationUrl = "https://github.com/login/oauth/authorize",
        TokenUrl = "https://github.com/login/oauth/access_token",
        DefaultScopes = new[] { "read:user" },
        IsActive = isActive,
        LastVerifiedAt = null,
        UpdatedById = Guid.NewGuid(),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static PlatformOAuthAppCredential ExistingCredential(
        Guid appId, int version = 1, bool isActive = true, string? privateKeyEncrypted = null) => new()
    {
        Id = Guid.NewGuid(),
        PlatformOAuthAppId = appId,
        ClientSecretEncrypted = "OLD-ENCRYPTED",
        PrivateKeyEncrypted = privateKeyEncrypted,
        EncryptionKeyVersion = "v1",
        CredentialVersion = version,
        IsActive = isActive,
        RotatedById = Guid.NewGuid(),
        RotatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static ConfigurePlatformOAuthAppCommand ConfigureCommand(
        string provider = "github",
        string? appName = null,
        string? logoUrl = null,
        string? clientId = null,
        string? clientSecret = null,
        string? privateKey = null,
        bool? isActive = null) => new(
        provider, appName, logoUrl, clientId, clientSecret, privateKey, isActive, Actor);

    // -- 1. Migration / EF mapping (unchanged schema) ---------------------------

    [Fact]
    public void Migration_CreatesOnlyTheTwoOAuthTables_AndNoOtherOperations()
    {
        var migration = new AddPlatformOAuthAppsTables();
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

        var up = typeof(AddPlatformOAuthAppsTables)
            .GetMethod("Up", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(up);
        up!.Invoke(migration, new object[] { builder });

        var createTables = builder.Operations.OfType<CreateTableOperation>().Select(o => o.Name).ToList();
        Assert.Equal(2, createTables.Count);
        Assert.Contains("platform_oauth_apps", createTables);
        Assert.Contains("platform_oauth_app_credentials", createTables);

        var touchedTables = builder.Operations
            .Select(o => o switch
            {
                CreateTableOperation ct => ct.Name,
                CreateIndexOperation ci => ci.Table,
                _ => null
            })
            .ToList();
        Assert.All(builder.Operations, o =>
            Assert.True(o is CreateTableOperation or CreateIndexOperation,
                $"Unexpected migration operation: {o.GetType().Name}"));
        Assert.All(touchedTables, t =>
            Assert.True(t is "platform_oauth_apps" or "platform_oauth_app_credentials",
                $"Migration touches unexpected table: {t}"));
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    [Fact]
    public void EfModel_PlatformOAuthApps_TableMappedWithUniqueProviderAndInventoryColumns()
    {
        using var db = BuildInMemoryDb();

        var entityType = db.Model.FindEntityType(typeof(PlatformOAuthApp));
        Assert.NotNull(entityType);
        Assert.Equal("platform_oauth_apps", entityType!.GetTableName());

        var providerProp = entityType.FindProperty(nameof(PlatformOAuthApp.Provider));
        Assert.NotNull(providerProp);
        Assert.False(providerProp!.IsNullable);
        Assert.Equal(30, providerProp.GetMaxLength());
        var uniqueIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(PlatformOAuthApp.Provider));
        Assert.NotNull(uniqueIndex);
        Assert.True(uniqueIndex!.IsUnique);

        Assert.Equal(100, entityType.FindProperty(nameof(PlatformOAuthApp.AppName))!.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(PlatformOAuthApp.LogoUrl))!.GetMaxLength());
        Assert.True(entityType.FindProperty(nameof(PlatformOAuthApp.LogoUrl))!.IsNullable);
        Assert.Equal(200, entityType.FindProperty(nameof(PlatformOAuthApp.ClientId))!.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(PlatformOAuthApp.AuthorizationUrl))!.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(PlatformOAuthApp.TokenUrl))!.GetMaxLength());
        Assert.False(entityType.FindProperty(nameof(PlatformOAuthApp.DefaultScopes))!.IsNullable);
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthApp.IsActive)));
        Assert.True(entityType.FindProperty(nameof(PlatformOAuthApp.LastVerifiedAt))!.IsNullable);
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthApp.UpdatedById)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthApp.UpdatedAt)));
    }

    [Fact]
    public void EfModel_PlatformOAuthAppCredentials_TableMappedWithInventoryColumns()
    {
        using var db = BuildInMemoryDb();

        var entityType = db.Model.FindEntityType(typeof(PlatformOAuthAppCredential));
        Assert.NotNull(entityType);
        Assert.Equal("platform_oauth_app_credentials", entityType!.GetTableName());

        var appIdProp = entityType.FindProperty(nameof(PlatformOAuthAppCredential.PlatformOAuthAppId));
        Assert.NotNull(appIdProp);
        Assert.Equal("platform_oauth_app_id", appIdProp!.GetColumnName());

        var secretProp = entityType.FindProperty(nameof(PlatformOAuthAppCredential.ClientSecretEncrypted));
        Assert.NotNull(secretProp);
        Assert.False(secretProp!.IsNullable);

        Assert.True(entityType.FindProperty(nameof(PlatformOAuthAppCredential.PrivateKeyEncrypted))!.IsNullable);
        Assert.Equal(50, entityType.FindProperty(nameof(PlatformOAuthAppCredential.EncryptionKeyVersion))!.GetMaxLength());
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthAppCredential.CredentialVersion)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthAppCredential.IsActive)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthAppCredential.RotatedById)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformOAuthAppCredential.RotatedAt)));
        Assert.True(entityType.FindProperty(nameof(PlatformOAuthAppCredential.DeactivatedById))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(PlatformOAuthAppCredential.DeactivatedAt))!.IsNullable);
    }

    [Fact]
    public void Schema_Entities_HaveOnlyInventoryColumns()
    {
        var appProps = typeof(PlatformOAuthApp).GetProperties().Select(p => p.Name).ToList();
        var appExpected = new[]
        {
            "Id", "Provider", "AppName", "LogoUrl", "ClientId", "AuthorizationUrl",
            "TokenUrl", "DefaultScopes", "IsActive", "LastVerifiedAt", "UpdatedById", "UpdatedAt"
        };
        Assert.Equal(appExpected.OrderBy(x => x), appProps.OrderBy(x => x));

        var credProps = typeof(PlatformOAuthAppCredential).GetProperties().Select(p => p.Name).ToList();
        var credExpected = new[]
        {
            "Id", "PlatformOAuthAppId", "ClientSecretEncrypted", "PrivateKeyEncrypted",
            "EncryptionKeyVersion", "CredentialVersion", "IsActive", "RotatedById",
            "RotatedAt", "DeactivatedById", "DeactivatedAt"
        };
        Assert.Equal(credExpected.OrderBy(x => x), credProps.OrderBy(x => x));
    }

    // -- 2. Provider catalog boundary ---------------------------------------------

    [Fact]
    public void Catalog_ContainsExactlyGithubGoogleMicrosoftZoom()
    {
        var providers = PlatformOAuthProviderCatalog.GetAll().Select(d => d.Provider).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { "github", "google", "microsoft", "zoom" }, providers);
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("aws")]
    [InlineData("aws_rekognition")]
    [InlineData("rekognition")]
    [InlineData("aws_s3")]
    [InlineData("provider_service")]
    [InlineData("not-a-real-provider")]
    public void Catalog_RejectsUnapprovedProviders(string provider)
    {
        Assert.False(PlatformOAuthProviderCatalog.IsApproved(provider));
    }

    [Fact]
    public void Catalog_Slack_IsPhase2NotApproved()
    {
        Assert.True(PlatformOAuthProviderCatalog.IsPhase2("slack"));
        Assert.False(PlatformOAuthProviderCatalog.IsApproved("slack"));
    }

    // -- 3. Configure (upsert) -----------------------------------------------------

    [Fact]
    public async Task Configure_NewProvider_CreatesRowFromCatalogMetadata_NotActiveByDefault()
    {
        PlatformOAuthApp? saved = null;
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<PlatformOAuthApp>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformOAuthApp, CancellationToken>((a, _) => saved = a)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(ConfigureCommand(clientId: "Iv1.new"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal("github", saved!.Provider);
        Assert.Equal("Iv1.new", saved.ClientId);
        Assert.Equal("https://github.com/login/oauth/authorize", saved.AuthorizationUrl);
        Assert.Equal("https://github.com/login/oauth/access_token", saved.TokenUrl);
        Assert.Equal(new[] { "read:user" }, saved.DefaultScopes);
        Assert.False(saved.IsActive);
        Assert.Equal("GitHub", saved.AppName); // defaults to catalog display name
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Configure_UnknownProvider_ReturnsValidationError()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());

        var result = await handler.Handle(ConfigureCommand(provider: "aws_rekognition"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.AddAsync(It.IsAny<PlatformOAuthApp>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Configure_SlackProvider_IsRejectedAsPhase2()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());

        var result = await handler.Handle(ConfigureCommand(provider: "slack"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.AddAsync(It.IsAny<PlatformOAuthApp>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Configure_WithClientSecret_EncryptsAndCreatesCredentialV1_NeverExposesPlaintext()
    {
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("plain-secret")).Returns("ENCRYPTED-SECRET");

        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<PlatformOAuthApp>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        PlatformOAuthAppCredential? savedCredential = null;
        repo.Setup(r => r.AddCredentialAsync(It.IsAny<PlatformOAuthAppCredential>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformOAuthAppCredential, CancellationToken>((c, _) => savedCredential = c)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetMaxCredentialVersionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, encryption.Object);
        var result = await handler.Handle(
            ConfigureCommand(clientId: "Iv1.abc", clientSecret: "plain-secret"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(savedCredential);
        Assert.Equal("ENCRYPTED-SECRET", savedCredential!.ClientSecretEncrypted);
        Assert.Equal(1, savedCredential.CredentialVersion);
        Assert.True(savedCredential.IsActive);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("plain-secret", serialized);
        Assert.DoesNotContain("ENCRYPTED-SECRET", serialized);
        Assert.True(result.Value!.HasActiveCredential);
    }

    [Fact]
    public async Task Configure_ExistingApp_AlwaysRefreshesProtocolMetadataFromCatalog()
    {
        // Even a row with drifted/legacy protocol fields is corrected back to the
        // backend-owned catalog values - the request has no way to influence them.
        var app = ExistingApp();
        app.AuthorizationUrl = "https://legacy.example/authorize";
        app.TokenUrl = "https://legacy.example/token";
        app.DefaultScopes = new[] { "legacy_scope" };

        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { ExistingCredential(app.Id) });

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(ConfigureCommand(appName: "Renamed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://github.com/login/oauth/authorize", app.AuthorizationUrl);
        Assert.Equal("https://github.com/login/oauth/access_token", app.TokenUrl);
        Assert.Equal(new[] { "read:user" }, app.DefaultScopes);
        Assert.Equal("Renamed", app.AppName);
    }

    [Fact]
    public async Task Configure_ExistingApp_RotatesCredentialWhenSecretProvided_DeactivatesOld()
    {
        var app = ExistingApp();
        var oldCredential = ExistingCredential(app.Id, version: 1);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("new-secret")).Returns("NEW-ENCRYPTED");

        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { oldCredential });
        repo.Setup(r => r.GetMaxCredentialVersionAsync(app.Id, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        PlatformOAuthAppCredential? added = null;
        repo.Setup(r => r.AddCredentialAsync(It.IsAny<PlatformOAuthAppCredential>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformOAuthAppCredential, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, encryption.Object);
        var result = await handler.Handle(ConfigureCommand(clientSecret: "new-secret"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(oldCredential.IsActive);
        Assert.NotNull(added);
        Assert.Equal(2, added!.CredentialVersion);
        Assert.Equal("NEW-ENCRYPTED", added.ClientSecretEncrypted);
    }

    [Fact]
    public async Task Configure_ActivateWithoutClientId_Fails()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<PlatformOAuthApp>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(ConfigureCommand(isActive: true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Configure_ActivateWithClientIdButNoCredential_Fails()
    {
        var app = ExistingApp(isActive: false);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(ConfigureCommand(isActive: true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.False(app.IsActive);
    }

    [Fact]
    public async Task Configure_ActivateWithClientIdAndSecretInSameRequest_Succeeds()
    {
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns("ENC");

        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<PlatformOAuthApp>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AddCredentialAsync(It.IsAny<PlatformOAuthAppCredential>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetMaxCredentialVersionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, encryption.Object);
        var result = await handler.Handle(
            ConfigureCommand(clientId: "Iv1.abc", clientSecret: "s3cret", isActive: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsActive);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Configure_DeactivateAlwaysAllowed()
    {
        var app = ExistingApp(isActive: true);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { ExistingCredential(app.Id) });

        var handler = new ConfigurePlatformOAuthAppCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(ConfigureCommand(isActive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(app.IsActive);
    }

    // -- 4. Rotate ------------------------------------------------------------

    [Fact]
    public async Task Rotate_UnknownProvider_ReturnsValidationErrorBeforeRepositoryLookup()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        var handler = new RotatePlatformOAuthAppSecretCommandHandler(repo.Object, Mock.Of<IEncryptionService>());

        var result = await handler.Handle(
            new RotatePlatformOAuthAppSecretCommand("aws_rekognition", "secret", null, Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_DeactivatesOldCredential_CreatesNewActiveVersion_NeverExposesSecrets()
    {
        var app = ExistingApp();
        var oldCredential = ExistingCredential(app.Id, version: 3);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("new-plain-secret")).Returns("NEW-ENCRYPTED");

        PlatformOAuthAppCredential? added = null;
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { oldCredential });
        repo.Setup(r => r.GetMaxCredentialVersionAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        repo.Setup(r => r.AddCredentialAsync(It.IsAny<PlatformOAuthAppCredential>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformOAuthAppCredential, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var handler = new RotatePlatformOAuthAppSecretCommandHandler(repo.Object, encryption.Object);
        var result = await handler.Handle(
            new RotatePlatformOAuthAppSecretCommand("github", "new-plain-secret", null, Actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.False(oldCredential.IsActive);
        Assert.Equal(Actor, oldCredential.DeactivatedById);
        Assert.NotNull(oldCredential.DeactivatedAt);
        Assert.Equal("OLD-ENCRYPTED", oldCredential.ClientSecretEncrypted);

        Assert.NotNull(added);
        Assert.Equal(4, added!.CredentialVersion);
        Assert.True(added.IsActive);
        Assert.Equal("NEW-ENCRYPTED", added.ClientSecretEncrypted);
        Assert.Equal(Actor, added.RotatedById);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("new-plain-secret", serialized);
        Assert.DoesNotContain("NEW-ENCRYPTED", serialized);
        Assert.DoesNotContain("OLD-ENCRYPTED", serialized);
        Assert.Equal(4, result.Value!.ActiveCredentialVersion);
    }

    [Fact]
    public async Task Rotate_ApprovedProviderWithoutRow_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);

        var handler = new RotatePlatformOAuthAppSecretCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(
            new RotatePlatformOAuthAppSecretCommand("zoom", "some-secret", null, Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Rotate_EmptyClientSecret_ReturnsValidationError()
    {
        var app = ExistingApp();
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var handler = new RotatePlatformOAuthAppSecretCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(
            new RotatePlatformOAuthAppSecretCommand("github", "", null, Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.AddCredentialAsync(It.IsAny<PlatformOAuthAppCredential>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -- 5. Activate / deactivate ------------------------------------------------

    [Fact]
    public async Task Activate_UnknownProvider_ReturnsValidationError()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        var handler = new SetPlatformOAuthAppActivationCommandHandler(repo.Object);

        var result = await handler.Handle(
            new SetPlatformOAuthAppActivationCommand("aws_rekognition", true, Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Activate_WithoutClientId_Fails()
    {
        var app = ExistingApp(isActive: false, clientId: "");
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { ExistingCredential(app.Id) });

        var handler = new SetPlatformOAuthAppActivationCommandHandler(repo.Object);
        var result = await handler.Handle(
            new SetPlatformOAuthAppActivationCommand("github", true, Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.False(app.IsActive);
    }

    [Fact]
    public async Task Activate_WithoutActiveCredential_Fails()
    {
        var app = ExistingApp(isActive: false);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new SetPlatformOAuthAppActivationCommandHandler(repo.Object);
        var result = await handler.Handle(
            new SetPlatformOAuthAppActivationCommand("github", true, Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.False(app.IsActive);
    }

    [Fact]
    public async Task Activate_WithClientIdAndActiveCredential_Succeeds()
    {
        var app = ExistingApp(isActive: false);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { ExistingCredential(app.Id) });

        var handler = new SetPlatformOAuthAppActivationCommandHandler(repo.Object);
        var result = await handler.Handle(
            new SetPlatformOAuthAppActivationCommand("github", true, Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(app.IsActive);
        Assert.Equal(Actor, app.UpdatedById);
    }

    [Fact]
    public async Task Deactivate_AlwaysAllowed_KeepsCredentialRows()
    {
        var app = ExistingApp(isActive: true);
        var credential = ExistingCredential(app.Id);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { credential });

        var handler = new SetPlatformOAuthAppActivationCommandHandler(repo.Object);
        var result = await handler.Handle(
            new SetPlatformOAuthAppActivationCommand("github", false, Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(app.IsActive);
        Assert.True(credential.IsActive);
    }

    [Fact]
    public async Task SetActivation_ApprovedProviderWithoutRow_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);

        var handler = new SetPlatformOAuthAppActivationCommandHandler(repo.Object);
        var result = await handler.Handle(
            new SetPlatformOAuthAppActivationCommand("zoom", true, Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    // -- 6. Validate config (local only) ------------------------------------------

    [Fact]
    public async Task ValidateConfig_AllLocalChecksPass_StampsLastVerifiedAt_ReturnsValidWithLocalType()
    {
        var app = ExistingApp();
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { ExistingCredential(app.Id) });

        var handler = new ValidatePlatformOAuthAppConfigCommandHandler(repo.Object);
        var result = await handler.Handle(
            new ValidatePlatformOAuthAppConfigCommand("github", Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("valid", result.Value!.Status);
        Assert.Equal("local", result.Value.VerificationType);
        Assert.NotNull(result.Value.VerifiedAt);
        Assert.NotNull(app.LastVerifiedAt);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateConfig_InactiveAppOrMissingCredential_ReturnsError_DoesNotStamp()
    {
        var app = ExistingApp(isActive: false);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ValidatePlatformOAuthAppConfigCommandHandler(repo.Object);
        var result = await handler.Handle(
            new ValidatePlatformOAuthAppConfigCommand("github", Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("error", result.Value!.Status);
        Assert.Equal("local", result.Value.VerificationType);
        Assert.Null(result.Value.VerifiedAt);
        Assert.Null(app.LastVerifiedAt);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateConfig_UnknownProvider_ReturnsValidationError()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        var handler = new ValidatePlatformOAuthAppConfigCommandHandler(repo.Object);

        var result = await handler.Handle(
            new ValidatePlatformOAuthAppConfigCommand("nope", Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task ValidateConfig_ApprovedProviderWithoutRow_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);

        var handler = new ValidatePlatformOAuthAppConfigCommandHandler(repo.Object);
        var result = await handler.Handle(
            new ValidatePlatformOAuthAppConfigCommand("zoom", Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    // -- 7. List / detail - catalog merge + DTO safety ----------------------------

    [Fact]
    public async Task List_ReturnsAllFourApprovedProviders_EvenWithNoDbRows()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthApp>());
        repo.Setup(r => r.ListActiveCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new ListPlatformOAuthAppsQueryHandler(repo.Object);
        var result = await handler.Handle(new ListPlatformOAuthAppsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count);
        Assert.Equal(new[] { "github", "google", "microsoft", "zoom" },
            result.Value.Select(d => d.Provider).OrderBy(p => p));
        Assert.All(result.Value, dto => Assert.False(dto.Configured));
    }

    [Fact]
    public async Task List_MergesDbRowIntoMatchingProviderCard()
    {
        var app = ExistingApp();
        var credential = ExistingCredential(app.Id);
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthApp> { app });
        repo.Setup(r => r.ListActiveCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { credential });

        var handler = new ListPlatformOAuthAppsQueryHandler(repo.Object);
        var result = await handler.Handle(new ListPlatformOAuthAppsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var githubCard = result.Value!.Single(d => d.Provider == "github");
        Assert.True(githubCard.Configured);
        Assert.True(githubCard.HasActiveCredential);
        var others = result.Value!.Where(d => d.Provider != "github");
        Assert.All(others, dto => Assert.False(dto.Configured));
    }

    [Fact]
    public async Task Get_UnknownProvider_ReturnsValidationError()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        var handler = new GetPlatformOAuthAppQueryHandler(repo.Object);

        var result = await handler.Handle(new GetPlatformOAuthAppQuery("nope"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Get_ApprovedProviderWithoutRow_ReturnsUnconfiguredCard()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);

        var handler = new GetPlatformOAuthAppQueryHandler(repo.Object);
        var result = await handler.Handle(new GetPlatformOAuthAppQuery("zoom"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("zoom", result.Value!.Provider);
        Assert.False(result.Value.Configured);
        Assert.Null(result.Value.ClientId);
    }

    [Fact]
    public async Task Get_ProviderLookup_IsCaseInsensitive()
    {
        var app = ExistingApp();
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new GetPlatformOAuthAppQueryHandler(repo.Object);
        var result = await handler.Handle(new GetPlatformOAuthAppQuery("GitHub"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("github", result.Value!.Provider);
    }

    [Fact]
    public async Task ListAndDetail_NeverContainSecretMaterial()
    {
        var app = ExistingApp();
        var credential = ExistingCredential(app.Id, privateKeyEncrypted: "PK-ENCRYPTED");
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthApp> { app });
        repo.Setup(r => r.ListActiveCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { credential });
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { credential });

        var listResult = await new ListPlatformOAuthAppsQueryHandler(repo.Object)
            .Handle(new ListPlatformOAuthAppsQuery(), CancellationToken.None);
        var getResult = await new GetPlatformOAuthAppQueryHandler(repo.Object)
            .Handle(new GetPlatformOAuthAppQuery("github"), CancellationToken.None);

        Assert.True(listResult.IsSuccess);
        Assert.True(getResult.IsSuccess);
        var githubCard = listResult.Value!.Single(d => d.Provider == "github");
        Assert.True(githubCard.HasActiveCredential);
        Assert.True(githubCard.HasPrivateKey);
        Assert.Equal(1, githubCard.ActiveCredentialVersion);

        var serialized = System.Text.Json.JsonSerializer.Serialize(listResult.Value)
            + System.Text.Json.JsonSerializer.Serialize(getResult.Value);
        Assert.DoesNotContain("OLD-ENCRYPTED", serialized);
        Assert.DoesNotContain("PK-ENCRYPTED", serialized);
        Assert.DoesNotContain("clientSecret\"", serialized);
        Assert.DoesNotContain("Encrypted", serialized);
    }

    [Fact]
    public void Security_ResponseDtos_DoNotExposeSecretFields()
    {
        var dtoTypes = new[] { typeof(PlatformOAuthAppDto), typeof(OAuthAppValidateConfigResultDto) };
        foreach (var dtoType in dtoTypes)
        {
            var props = dtoType.GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            // clientSecretRequired is a safe boolean flag (whether the provider needs a
            // secret at all) - it never carries secret material, unlike clientSecret*.
            Assert.DoesNotContain(props, p => p.Contains("secret") && p != "clientsecretrequired");
            Assert.DoesNotContain(props, p => p.Contains("encrypted"));
            Assert.DoesNotContain(props, p => p == "privatekey");
        }
    }

    // -- 8. Resolver (server-side only, unchanged surface) ------------------------

    [Fact]
    public async Task Resolver_ReturnsActiveAppMetadata_NullForInactiveOrUnknown()
    {
        var active = ExistingApp("github", isActive: true);
        var inactive = ExistingApp("zoom", isActive: false);

        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>())).ReturnsAsync(active);
        repo.Setup(r => r.GetByProviderAsync("zoom", It.IsAny<CancellationToken>())).ReturnsAsync(inactive);
        repo.Setup(r => r.GetByProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);

        var resolver = new PlatformOAuthAppResolver(repo.Object, Mock.Of<IEncryptionService>());

        var resolved = await resolver.GetActiveAppForProviderAsync("github", CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal("github", resolved!.Provider);
        Assert.Equal(active.ClientId, resolved.ClientId);
        Assert.Equal(active.AuthorizationUrl, resolved.AuthorizationUrl);
        Assert.Equal(active.TokenUrl, resolved.TokenUrl);
        Assert.Equal(active.DefaultScopes, resolved.DefaultScopes);

        Assert.Null(await resolver.GetActiveAppForProviderAsync("zoom", CancellationToken.None));
        Assert.Null(await resolver.GetActiveAppForProviderAsync("google", CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_DecryptsCredentialServerSideOnly_NullWithoutActiveCredential()
    {
        var app = ExistingApp("github", isActive: true);
        var credential = ExistingCredential(app.Id, version: 2, privateKeyEncrypted: "PK-ENCRYPTED");

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt("OLD-ENCRYPTED")).Returns("decrypted-secret");
        encryption.Setup(e => e.Decrypt("PK-ENCRYPTED")).Returns("decrypted-pk");

        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("github", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { credential });

        var resolver = new PlatformOAuthAppResolver(repo.Object, encryption.Object);
        var resolved = await resolver.GetActiveCredentialForProviderAsync("github", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("decrypted-secret", resolved!.ClientSecret);
        Assert.Equal("decrypted-pk", resolved.PrivateKey);
        Assert.Equal(2, resolved.CredentialVersion);

        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());
        Assert.Null(await resolver.GetActiveCredentialForProviderAsync("github", CancellationToken.None));
    }

    // -- 9. Permissions / route surface --------------------------------------------

    [Fact]
    public void Authorization_Endpoints_RequireAdminPolicyAndCorrectPlatformPermission()
    {
        var controllerType = typeof(PlatformOAuthAppsController);

        var authorizeAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("AdminPolicy", authorizeAttr!.Policy);

        var readEndpoints = new[] { "ListOAuthApps", "GetOAuthApp" };
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(7, methods.Length);

        foreach (var method in methods)
        {
            var permissionAttr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
            Assert.NotNull(permissionAttr);

            if (readEndpoints.Contains(method.Name))
                Assert.Equal(PlatformPermissionCatalog.SystemConfigRead, permissionAttr!.Permission);
            else
                Assert.Equal(PlatformPermissionCatalog.SystemConfigManage, permissionAttr!.Permission);
        }
    }

    [Fact]
    public void Controller_HasNoArbitraryCreateEndpoint()
    {
        var controllerType = typeof(PlatformOAuthAppsController);
        var httpPostMethods = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes()
                .Any(a => a.GetType().Name == "HttpPostAttribute"))
            .ToList();

        // Every POST action must target a specific {provider} sub-route
        // (rotate-secret / activate / deactivate / validate-config) - none may create
        // an arbitrary provider via a bare POST /oauth-apps.
        Assert.DoesNotContain(httpPostMethods, m => m.Name == "CreateOAuthApp");
    }
}
