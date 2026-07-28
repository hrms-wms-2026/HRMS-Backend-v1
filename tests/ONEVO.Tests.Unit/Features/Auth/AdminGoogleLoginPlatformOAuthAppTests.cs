using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class AdminGoogleLoginPlatformOAuthAppTests
{
    [Fact]
    public async Task ActiveGoogleApp_ClientIdIsUsedForTokenValidation()
    {
        var google = new Mock<IGoogleIdTokenValidator>();
        var oauthApps = new Mock<IPlatformOAuthAppResolver>();
        var authEvents = new Mock<IPlatformAuthEventRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        oauthApps
            .Setup(resolver => resolver.GetActiveAppForProviderAsync(
                "google",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp(
                "google",
                "database-google-client-id",
                "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token",
                []));
        google
            .Setup(validator => validator.ValidateAsync(
                "google-id-token",
                "database-google-client-id",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleIdTokenPayload?)null);
        authEvents
            .Setup(repository => repository.AddAsync(
                It.IsAny<ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities.PlatformAuthEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new AdminGoogleLoginCommandHandler(
            google.Object,
            oauthApps.Object,
            Mock.Of<ISecureTokenGenerator>(),
            Mock.Of<IDateTimeProvider>(),
            Mock.Of<IPlatformUserRepository>(),
            Mock.Of<IPlatformPermissionResolver>(),
            authEvents.Object,
            unitOfWork.Object);

        var result = await handler.Handle(
            new AdminGoogleLoginCommand(
                "google-id-token",
                "127.0.0.1",
                "test-agent"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        google.Verify(
            validator => validator.ValidateAsync(
                "google-id-token",
                "database-google-client-id",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MissingActiveGoogleApp_FailsSafelyBeforeTokenValidation()
    {
        var google = new Mock<IGoogleIdTokenValidator>();
        var oauthApps = new Mock<IPlatformOAuthAppResolver>();
        oauthApps
            .Setup(resolver => resolver.GetActiveAppForProviderAsync(
                "google",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthApp?)null);

        var handler = new AdminGoogleLoginCommandHandler(
            google.Object,
            oauthApps.Object,
            Mock.Of<ISecureTokenGenerator>(),
            Mock.Of<IDateTimeProvider>(),
            Mock.Of<IPlatformUserRepository>(),
            Mock.Of<IPlatformPermissionResolver>(),
            Mock.Of<IPlatformAuthEventRepository>(),
            Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new AdminGoogleLoginCommand(
                "google-id-token",
                "127.0.0.1",
                "test-agent"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        Assert.DoesNotContain(
            "secret",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        google.Verify(
            validator => validator.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
