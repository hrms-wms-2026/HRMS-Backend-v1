using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Identity;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class AdminDatabaseTicketStoreTests
{
    private static readonly DateTimeOffset FixedNow = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    private readonly IPlatformUserSessionRepository _sessions = Substitute.For<IPlatformUserSessionRepository>();
    private readonly IPlatformPermissionResolver _resolver = Substitute.For<IPlatformPermissionResolver>();
    private readonly IPlatformAuthEventRepository _authEvents = Substitute.For<IPlatformAuthEventRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public AdminDatabaseTicketStoreTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);

        _serviceProvider.GetService(typeof(IPlatformUserSessionRepository)).Returns(_sessions);
        _serviceProvider.GetService(typeof(IPlatformPermissionResolver)).Returns(_resolver);
        _serviceProvider.GetService(typeof(IPlatformAuthEventRepository)).Returns(_authEvents);
        _serviceProvider.GetService(typeof(IUnitOfWork)).Returns(_uow);

        _clock.UtcNow.Returns(FixedNow);
    }

    private AdminDatabaseTicketStore CreateSut() =>
        new(_scopeFactory, _clock);

    private static AuthenticationTicket CreateTicket(Guid platformUserId, string csrfHash)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, platformUserId.ToString()),
            new Claim(ClaimTypes.Email, "admin@onevo.com")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));
        var properties = new AuthenticationProperties();
        properties.Items[AdminDatabaseTicketStore.CsrfTokenHashItemKey] = csrfHash;
        return new AuthenticationTicket(principal, properties, "AdminScheme");
    }

    [Fact]
    public async Task StoreAsync_PersistsSession_InPlatformUserSessions_WithHashedTokenOnly()
    {
        var platformUserId = Guid.NewGuid();
        var ticket = CreateTicket(platformUserId, "csrf-hash");

        PlatformUserSession? captured = null;
        _sessions.AddAsync(Arg.Do<PlatformUserSession>(s => captured = s), Arg.Any<CancellationToken>())
                 .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var rawKey = await sut.StoreAsync(ticket, null, CancellationToken.None);

        Assert.NotNull(rawKey);
        Assert.NotNull(captured);
        Assert.Equal(platformUserId, captured!.AccountId);
        Assert.Equal("csrf-hash", captured.CsrfTokenHash);

        // Only the SHA-256 hash of the opaque key may be stored — never the raw key.
        Assert.NotEmpty(captured.TokenHash);
        Assert.NotEqual(rawKey, captured.TokenHash);
        Assert.Equal(64, captured.TokenHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", captured.TokenHash);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetrieveAsync_ValidSessionAndActiveUser_ReturnsTicketWithPermissionClaims()
    {
        var platformUserId = Guid.NewGuid();
        var session = new PlatformUserSession
        {
            Id = Guid.NewGuid(),
            AccountId = platformUserId,
            TokenHash = "irrelevant",
            CreatedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10),
            CsrfTokenHash = "csrf-hash"
        };

        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);
        _resolver.ResolveActiveUserAsync(platformUserId, Arg.Any<CancellationToken>())
                 .Returns(new PlatformAccessProfile
                 {
                     UserId = platformUserId,
                     Email = "admin@onevo.com",
                     Status = PlatformUser.StatusActive,
                     RoleNames = { "Platform Super Admin" },
                     PermissionCodes = { PlatformPermissionCatalog.TenantsRead, PlatformPermissionCatalog.TenantsManage }
                 });

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.NotNull(ticket);
        Assert.Equal(platformUserId.ToString(), ticket!.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("admin@onevo.com", ticket.Principal.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("Platform Super Admin", ticket.Principal.FindFirstValue("platform_role"));
        Assert.Equal("csrf-hash", ticket.Properties.Items[AdminDatabaseTicketStore.CsrfTokenHashItemKey]);

        var permissionClaims = ticket.Principal.Claims
            .Where(c => c.Type == PlatformPermissionCatalog.PermissionClaimType)
            .Select(c => c.Value)
            .ToHashSet();
        Assert.Contains(PlatformPermissionCatalog.TenantsRead, permissionClaims);
        Assert.Contains(PlatformPermissionCatalog.TenantsManage, permissionClaims);
    }

    [Fact]
    public async Task RetrieveAsync_ExpiredSession_ReturnsNull()
    {
        var session = new PlatformUserSession
        {
            AccountId = Guid.NewGuid(),
            CreatedAt = FixedNow.AddMinutes(-30),
            ExpiresAt = FixedNow.AddMinutes(-1)
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task RetrieveAsync_RevokedSession_ReturnsNull()
    {
        var session = new PlatformUserSession
        {
            AccountId = Guid.NewGuid(),
            CreatedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10),
            RevokedAt = FixedNow.AddMinutes(-1)
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task RetrieveAsync_PastAbsoluteLifetime_ReturnsNull()
    {
        var session = new PlatformUserSession
        {
            AccountId = Guid.NewGuid(),
            CreatedAt = FixedNow.Subtract(SessionPolicy.AbsoluteLifetime).AddMinutes(-1),
            ExpiresAt = FixedNow.AddMinutes(10)
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task RetrieveAsync_InactiveOrUnknownUser_ReturnsNull()
    {
        var session = new PlatformUserSession
        {
            AccountId = Guid.NewGuid(),
            CreatedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10)
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);
        _resolver.ResolveActiveUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                 .Returns((PlatformAccessProfile?)null);

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task RenewAsync_ExtendsExpiry_ReissuesCsrfCookie()
    {
        var session = new PlatformUserSession
        {
            AccountId = Guid.NewGuid(),
            CreatedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(5),
            CsrfTokenHash = "hash"
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var httpContext = new DefaultHttpContext();
        var env = Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var mockSp = Substitute.For<IServiceProvider>();
        mockSp.GetService(typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment)).Returns(env);
        httpContext.RequestServices = mockSp;
        httpContext.Request.Headers["Cookie"] = "admin_csrf=existing_csrf_val";

        var sut = CreateSut();
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(), new AuthenticationProperties(), "AdminScheme");

        await sut.RenewAsync("some-raw-key", ticket, httpContext, CancellationToken.None);

        Assert.Equal(FixedNow.Add(SessionPolicy.SlidingWindow), session.ExpiresAt);
        Assert.Equal(FixedNow, session.LastActivityAt);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("admin_csrf=existing_csrf_val", setCookieHeader);
    }

    [Fact]
    public async Task RemoveAsync_RevokesSession_AndWritesSessionRevokedAuthEvent()
    {
        var accountId = Guid.NewGuid();
        var session = new PlatformUserSession
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CreatedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10)
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        PlatformAuthEvent? capturedEvent = null;
        _authEvents.AddAsync(Arg.Do<PlatformAuthEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
                   .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.RemoveAsync("some-raw-key", null, CancellationToken.None);

        await _sessions.Received(1).RevokeByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        Assert.NotNull(capturedEvent);
        Assert.Equal(PlatformAuthEvent.SessionRevoked, capturedEvent!.EventType);
        Assert.Equal(accountId, capturedEvent.UserId);
        // Metadata must never contain secrets or raw tokens.
        Assert.DoesNotContain("some-raw-key", capturedEvent.MetadataJson ?? string.Empty);
    }
}
