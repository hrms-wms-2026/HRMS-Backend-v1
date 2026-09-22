using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CompleteCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICalendarOAuthStateProtector> _stateProtector = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IPlatformOAuthAppResolver> _appResolver = new();
    private readonly Mock<ICalendarOAuthTokenExchangeClient> _tokenClient = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<CompleteCalendarConnectionCommandHandler>> _logger = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Urls:CalendarOAuthCallbackBaseUrl"] = "https://localhost:7229",
            ["Urls:AppBaseUrl"] = "https://onexso.com:4200",
            ["Tenancy:RootDomain"] = "onexso.com"
        })
        .Build();

    private CompleteCalendarConnectionCommandHandler BuildSut()
    {
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<string>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<string>>>, CancellationToken>((action, ct) => action(ct));
        return new CompleteCalendarConnectionCommandHandler(
            _stateProtector.Object, _tenants.Object, _tenantSwitcher.Object, _appResolver.Object,
            _tokenClient.Object, _connections.Object, _encryption.Object, _unitOfWork.Object, _configuration,
            _logger.Object);
    }

    private static void VerifyLogged(Mock<ILogger<CompleteCalendarConnectionCommandHandler>> logger, LogLevel level, Times times) =>
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((_, _) => true)),
            times);

    private static CalendarOAuthState ValidState(DateTimeOffset? expiresAtUtc = null) => new(
        "nonce", TenantId, UserId, "google", DateTimeOffset.UtcNow, expiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public async Task Handle_UnparseableState_ReturnsFailure400()
    {
        var sut = BuildSut();
        _stateProtector.Setup(x => x.TryUnprotect("bad-state", out It.Ref<CalendarOAuthState?>.IsAny)).Returns(false);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "bad-state"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ExpiredState_ReturnsFailure400()
    {
        var sut = BuildSut();
        var state = ValidState(expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TenantNotFound_ReturnsFailure400()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TokenExchangeThrows_ReturnsSuccessWithErrorRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _tokenClient.Setup(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider unavailable"));

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connectionError=1", result.Value);
        Assert.Contains("acme.onexso.com", result.Value);
        VerifyLogged(_logger, LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_NullCode_ReturnsSuccessWithErrorRedirect()
    {
        // Google/Microsoft redirect here with no `code` param at all when the user denies
        // consent (error=access_denied&state=...). We already have a valid, decrypted state
        // and tenant at this point, so this must NOT be a 400 — it must be the same
        // errorRedirect used for other post-tenant-switch failures.
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", null, "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connectionError=1", result.Value);
        Assert.Contains("acme.onexso.com", result.Value);
        _appResolver.Verify(x => x.GetActiveAppForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyCode_ReturnsSuccessWithErrorRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", string.Empty, "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connectionError=1", result.Value);
        Assert.Contains("acme.onexso.com", result.Value);
    }

    [Fact]
    public async Task Handle_UnconfiguredProvider_LogsWarningAndReturnsSuccessWithErrorRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthApp?)null);
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedPlatformOAuthAppCredential?)null);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connectionError=1", result.Value);
        VerifyLogged(_logger, LogLevel.Warning, Times.Once());
    }

    [Fact]
    public async Task Handle_NoRefreshTokenOnNewConnection_LogsWarningAndReturnsSuccessWithErrorRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _tokenClient.Setup(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderTokens("at-1", null, DateTimeOffset.UtcNow.AddHours(1)));
        _tokenClient.Setup(x => x.GetAccountAsync("google", "at-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderAccount("me@acme.com", "me@acme.com", "me@acme.com"));
        _connections.Setup(x => x.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.GoogleCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connectionError=1", result.Value);
        VerifyLogged(_logger, LogLevel.Warning, Times.Once());
        _connections.Verify(x => x.AddAsync(It.IsAny<ExternalCalendarConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCancellationRequested_RethrowsOperationCanceledException()
    {
        // A bare `catch` used to swallow OperationCanceledException and report it as a
        // "success" redirect. A genuine client-disconnect cancellation must propagate instead.
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), cts.Token));
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesConnectionAndReturnsSuccessRedirect()
    {
        var sut = BuildSut();
        var state = ValidState();
        _stateProtector.Setup(x => x.TryUnprotect("state", out state)).Returns(true);
        var tenant = new Tenant { Id = TenantId, Slug = "acme", Status = TenantStatus.Active };
        _tenants.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _appResolver.Setup(x => x.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", ["scope"]));
        _appResolver.Setup(x => x.GetActiveCredentialForProviderAsync("google", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedPlatformOAuthAppCredential("google", "client", "secret", null, 1));
        _tokenClient.Setup(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderTokens("at-1", "rt-1", DateTimeOffset.UtcNow.AddHours(1)));
        _tokenClient.Setup(x => x.GetAccountAsync("google", "at-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarProviderAccount("me@acme.com", "me@acme.com", "me@acme.com"));
        _connections.Setup(x => x.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.GoogleCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);
        _encryption.Setup(x => x.EncryptBytes(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));

        var result = await sut.Handle(new CompleteCalendarConnectionCommand("google", "code", "state"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("connected=google", result.Value);
        Assert.Contains("acme.onexso.com", result.Value);
        _connections.Verify(x => x.AddAsync(It.Is<ExternalCalendarConnection>(c =>
            c.TenantId == TenantId && c.UserId == UserId && c.ExternalAccountEmail == "me@acme.com"), It.IsAny<CancellationToken>()), Times.Once);
        _tenantSwitcher.Verify(x => x.SwitchToTenantAsync(It.Is<TenantRegistryEntry>(t => t.TenantId == TenantId && t.Slug == "acme"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
