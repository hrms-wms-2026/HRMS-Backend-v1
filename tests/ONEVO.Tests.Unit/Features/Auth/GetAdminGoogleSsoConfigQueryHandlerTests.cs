using Moq;
using ONEVO.Application.Features.Auth.Login.Queries.GetAdminGoogleSsoConfig;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// GET /admin/v1/auth/google-config never returns secret material and only reports
/// enabled=true when google is an approved, active, fully-configured provider.
/// </summary>
public sealed class GetAdminGoogleSsoConfigQueryHandlerTests
{
    private static PlatformOAuthApp GoogleApp(bool isActive, string clientId) => new()
    {
        Id = Guid.NewGuid(),
        Provider = "google",
        AppName = "Google",
        ClientId = clientId,
        AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenUrl = "https://oauth2.googleapis.com/token",
        DefaultScopes = new[] { "openid", "profile", "email" },
        IsActive = isActive,
        UpdatedById = Guid.NewGuid()
    };

    private static PlatformOAuthAppCredential ActiveCredential(Guid appId) => new()
    {
        Id = Guid.NewGuid(),
        PlatformOAuthAppId = appId,
        ClientSecretEncrypted = "ENCRYPTED",
        EncryptionKeyVersion = "v1",
        CredentialVersion = 1,
        IsActive = true,
        RotatedById = Guid.NewGuid()
    };

    [Fact]
    public async Task NoGoogleRow_ReturnsDisabled()
    {
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOAuthApp?)null);

        var handler = new GetAdminGoogleSsoConfigQueryHandler(repo.Object);
        var result = await handler.Handle(new GetAdminGoogleSsoConfigQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Enabled);
        Assert.Null(result.Value.ClientId);
    }

    [Fact]
    public async Task InactiveGoogleRow_ReturnsDisabled()
    {
        var app = GoogleApp(isActive: false, clientId: "some-client-id");
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("google", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var handler = new GetAdminGoogleSsoConfigQueryHandler(repo.Object);
        var result = await handler.Handle(new GetAdminGoogleSsoConfigQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Enabled);
    }

    [Fact]
    public async Task ActiveWithoutClientId_ReturnsDisabled()
    {
        var app = GoogleApp(isActive: true, clientId: "");
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("google", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var handler = new GetAdminGoogleSsoConfigQueryHandler(repo.Object);
        var result = await handler.Handle(new GetAdminGoogleSsoConfigQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Enabled);
    }

    [Fact]
    public async Task ActiveWithClientIdButNoActiveCredential_ReturnsDisabled()
    {
        var app = GoogleApp(isActive: true, clientId: "some-client-id");
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("google", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential>());

        var handler = new GetAdminGoogleSsoConfigQueryHandler(repo.Object);
        var result = await handler.Handle(new GetAdminGoogleSsoConfigQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Enabled);
        Assert.Null(result.Value.ClientId);
    }

    [Fact]
    public async Task FullyConfiguredActiveGoogleApp_ReturnsEnabledWithClientId_NeverSecrets()
    {
        var app = GoogleApp(isActive: true, clientId: "public-client-id.apps.googleusercontent.com");
        var repo = new Mock<IPlatformOAuthAppRepository>();
        repo.Setup(r => r.GetByProviderAsync("google", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        repo.Setup(r => r.GetActiveCredentialsForAppAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformOAuthAppCredential> { ActiveCredential(app.Id) });

        var handler = new GetAdminGoogleSsoConfigQueryHandler(repo.Object);
        var result = await handler.Handle(new GetAdminGoogleSsoConfigQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Enabled);
        Assert.Equal("public-client-id.apps.googleusercontent.com", result.Value.ClientId);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("ENCRYPTED", serialized);
        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
