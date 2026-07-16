using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Identity;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class TenantDatabaseTicketStoreTests
{
    private static readonly DateTimeOffset FixedNow = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPermissionResolver _permissions = Substitute.For<IPermissionResolver>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public TenantDatabaseTicketStoreTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);

        _serviceProvider.GetService(typeof(ISessionRepository)).Returns(_sessions);
        _serviceProvider.GetService(typeof(IUserRepository)).Returns(_users);
        _serviceProvider.GetService(typeof(IPermissionResolver)).Returns(_permissions);
        _serviceProvider.GetService(typeof(IUnitOfWork)).Returns(_uow);
        
        _clock.UtcNow.Returns(FixedNow);
    }

    private TenantDatabaseTicketStore CreateSut() =>
        new(_scopeFactory, _clock);

    [Fact]
    public async Task StoreAsync_PersistsSession_ReturnsRawKey()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var csrfHash = "csrf-hash";

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));
        var properties = new AuthenticationProperties();
        properties.Items[TenantDatabaseTicketStore.TenantIdItemKey] = tenantId.ToString();
        properties.Items[TenantDatabaseTicketStore.CsrfTokenHashItemKey] = csrfHash;

        var ticket = new AuthenticationTicket(principal, properties, "TenantScheme");

        Session? captured = null;
        _sessions.AddAsync(Arg.Do<Session>(s => captured = s), Arg.Any<CancellationToken>())
                 .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var rawKey = await sut.StoreAsync(ticket, null, CancellationToken.None);

        Assert.NotNull(rawKey);
        Assert.NotNull(captured);
        Assert.Equal(userId, captured!.UserId);
        Assert.Equal(tenantId, captured.TenantId);
        Assert.Equal(csrfHash, captured.CsrfTokenHash);
        Assert.NotEmpty(captured.KeyHash);
        
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetrieveAsync_ValidKey_ReturnsPopulatedTicket()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var session = new Session
        {
            UserId = userId,
            TenantId = tenantId,
            IsRevoked = false,
            StartedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10),
            CsrfTokenHash = "csrf-hash"
        };

        _sessions.GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var user = new ONEVO.Domain.Features.InfrastructureModule.Entities.User
            { Id = userId, Email = "a@b.com", IsActive = true };
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        _permissions.ResolveAsync(userId, tenantId, Arg.Any<CancellationToken>())
                    .Returns(["perm.read"]);

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.NotNull(ticket);
        Assert.Equal(userId.ToString(), ticket!.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("a@b.com", ticket.Principal.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("perm.read", ticket.Principal.FindFirstValue("permission"));
        Assert.Equal(tenantId.ToString(), ticket.Properties.Items[TenantDatabaseTicketStore.TenantIdItemKey]);
        Assert.Equal("csrf-hash", ticket.Properties.Items[TenantDatabaseTicketStore.CsrfTokenHashItemKey]);
    }

    [Fact]
    public async Task RetrieveAsync_ExpiredSession_ReturnsNull()
    {
        var session = new Session
        {
            IsRevoked = false,
            StartedAt = FixedNow.AddMinutes(-30),
            ExpiresAt = FixedNow.AddMinutes(-1)
        };
        _sessions.GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var sut = CreateSut();
        var ticket = await sut.RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.Null(ticket);
    }

    [Fact]
    public async Task RetrieveAsync_RevokedSession_ReturnsNull()
    {
        var session = new Session
        {
            IsRevoked = true,
            StartedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10)
        };
        _sessions.GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(session);

        var ticket = await CreateSut().RetrieveAsync(
            "revoked-session-key",
            null,
            CancellationToken.None);

        Assert.Null(ticket);
        await _permissions.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default, default, default);
    }

    [Fact]
    public async Task RenewAsync_ExtendsExpiry_ReissuesCsrfCookie()
    {
        var session = new Session
        {
            IsRevoked = false,
            StartedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(5),
            CsrfTokenHash = "hash"
        };
        _sessions.GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);

        var httpContext = new DefaultHttpContext();
        var env = Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var mockSp = Substitute.For<IServiceProvider>();
        mockSp.GetService(typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment)).Returns(env);
        httpContext.RequestServices = mockSp;
        httpContext.Request.Headers["Cookie"] = "onevo_csrf=existing_csrf_val";

        
        var sut = CreateSut();
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(), new AuthenticationProperties(), "TenantScheme");
        
        await sut.RenewAsync("some-raw-key", ticket, httpContext, CancellationToken.None);

        Assert.Equal(FixedNow.Add(SessionPolicy.SlidingWindow), session.ExpiresAt);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        
        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("onevo_csrf=existing_csrf_val", setCookieHeader);
        Assert.Contains("expires=", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveAsync_RevokesSession()
    {
        var sut = CreateSut();
        await sut.RemoveAsync("some-raw-key", null, CancellationToken.None);

        await _sessions.Received(1).RevokeByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
