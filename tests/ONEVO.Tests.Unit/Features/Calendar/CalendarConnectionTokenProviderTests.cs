using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarConnectionTokenProviderTests
{
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarOAuthTokenExchangeClient> _tokenExchange = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CalendarConnectionTokenProvider BuildSut() => new(
        _connections.Object, _tokenExchange.Object, _appResolver.Object, _encryption.Object, _unitOfWork.Object,
        NullLogger<CalendarConnectionTokenProvider>.Instance);

    [Fact]
    public async Task GetFreshAccessTokenAsync_TokenNotExpired_ReturnsDecryptedTokenWithoutRefreshing()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), AccessTokenEncrypted = [1, 2, 3],
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        _encryption.Setup(e => e.DecryptBytes(connection.AccessTokenEncrypted!)).Returns("valid-token");

        var result = await BuildSut().GetFreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Equal("valid-token", result);
        _tokenExchange.Verify(t => t.RefreshTokenAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetFreshAccessTokenAsync_TokenExpired_RefreshesAndPersists()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), RefreshTokenEncrypted = [9, 9, 9],
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        _appResolver.Setup(a => a.GetActiveAppForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("microsoft", "client-id", "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", ["scope"]));
        _appResolver.Setup(a => a.GetActiveCredentialForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("microsoft", "client-id", "client-secret", null, 1));
        _encryption.Setup(e => e.DecryptBytes(connection.RefreshTokenEncrypted)).Returns("old-refresh-token");
        _tokenExchange.Setup(t => t.RefreshTokenAsync(
                "https://login.microsoftonline.com/common/oauth2/v2.0/token", "client-id", "client-secret", "old-refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderTokens("new-access-token", "new-refresh-token", DateTimeOffset.UtcNow.AddHours(1)));
        _encryption.Setup(e => e.EncryptBytes("new-access-token")).Returns([1]);
        _encryption.Setup(e => e.EncryptBytes("new-refresh-token")).Returns([2]);

        var result = await BuildSut().GetFreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Equal("new-access-token", result);
        _connections.Verify(c => c.Update(connection), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFreshAccessTokenAsync_RefreshFails_MarksReauthRequiredAndReturnsNull()
    {
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), RefreshTokenEncrypted = [9], ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        _appResolver.Setup(a => a.GetActiveAppForProviderAsync("microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthApp?)null);

        var result = await BuildSut().GetFreshAccessTokenAsync(connection, "microsoft", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(ExternalCalendarConnectionStatuses.ReauthRequired, connection.Status);
        _connections.Verify(c => c.Update(connection), Times.Once);
    }
}
