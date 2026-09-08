using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class StartCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<ICalendarOAuthStateProtector> _stateProtector = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Urls:CalendarOAuthCallbackBaseUrl"] = "https://localhost:7229" })
        .Build();

    private StartCalendarConnectionCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new StartCalendarConnectionCommandHandler(_currentUser.Object, _appResolver.Object, _stateProtector.Object, _configuration);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new StartCalendarConnectionCommandHandler(_currentUser.Object, _appResolver.Object, _stateProtector.Object, _configuration);

        var result = await sut.Handle(new StartCalendarConnectionCommand("google"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData("dropbox")]
    [InlineData("")]
    public async Task Handle_UnsupportedProvider_ReturnsFailure(string provider)
    {
        var sut = BuildSut();

        var result = await sut.Handle(new StartCalendarConnectionCommand(provider), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProviderNotConfigured_ReturnsFailure()
    {
        var sut = BuildSut();
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthApp?)null);

        var result = await sut.Handle(new StartCalendarConnectionCommand("google"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidProvider_ReturnsAuthorizeUrlContainingStateAndRedirectUri()
    {
        var sut = BuildSut();
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client-123", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["https://www.googleapis.com/auth/calendar"]));
        _stateProtector.Setup(x => x.Protect(It.Is<CalendarOAuthState>(s => s.TenantId == TenantId && s.UserId == UserId && s.Provider == "google")))
            .Returns("protected-state-token");

        var result = await sut.Handle(new StartCalendarConnectionCommand("google"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", result.Value!.AuthorizeUrl);
        Assert.Contains("client_id=client-123", result.Value.AuthorizeUrl);
        Assert.Contains("state=protected-state-token", result.Value.AuthorizeUrl);

        // HttpUtility.ParseQueryString uses lowercase hex encoding, so compare case-insensitively
        var redirectUriEscaped = Uri.EscapeDataString("https://localhost:7229/api/v1/calendar/connections/google/callback");
        var urlLower = result.Value.AuthorizeUrl.ToLowerInvariant();
        var escapedLower = redirectUriEscaped.ToLowerInvariant();
        Assert.Contains(escapedLower, urlLower);
    }
}
