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
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Sessions;
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
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantContextSwitcher _tenantSwitcher = Substitute.For<ITenantContextSwitcher>();

    public TenantDatabaseTicketStoreTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);

        _serviceProvider.GetService(typeof(ISessionRepository)).Returns(_sessions);
        _serviceProvider.GetService(typeof(IUserRepository)).Returns(_users);
        _serviceProvider.GetService(typeof(IPermissionResolver)).Returns(_permissions);
        _serviceProvider.GetService(typeof(IUnitOfWork)).Returns(_uow);
        _serviceProvider.GetService(typeof(ITenantRepository)).Returns(_tenants);
        _serviceProvider.GetService(typeof(ITenantContextSwitcher)).Returns(_tenantSwitcher);

        _clock.UtcNow.Returns(FixedNow);
    }

    private static Tenant ActiveTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Slug = "acme",
        Status = TenantStatus.Active
    };

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

        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(ActiveTenant(tenantId));

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
    public async Task StoreAsync_SwitchesToTenantContext_BeforeSavingSession()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));
        var properties = new AuthenticationProperties();
        properties.Items[TenantDatabaseTicketStore.TenantIdItemKey] = tenantId.ToString();
        var ticket = new AuthenticationTicket(principal, properties, "TenantScheme");

        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(ActiveTenant(tenantId));
        _sessions.AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.StoreAsync(ticket, null, CancellationToken.None);

        await _tenantSwitcher.Received(1).SwitchToTenantAsync(
            Arg.Is<TenantRegistryEntry>(t => t.TenantId == tenantId),
            Arg.Any<CancellationToken>());

        Received.InOrder(async () =>
        {
            await _tenantSwitcher.SwitchToTenantAsync(Arg.Any<TenantRegistryEntry>(), Arg.Any<CancellationToken>());
            await _sessions.AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
            await _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Theory]
    [InlineData(true, TenantStatus.Cancelled)]
    [InlineData(true, TenantStatus.Suspended)]
    [InlineData(false, TenantStatus.Active)]
    public async Task StoreAsync_TenantMissingOrDead_DoesNotCreateSession(bool tenantExists, TenantStatus status)
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));
        var properties = new AuthenticationProperties();
        properties.Items[TenantDatabaseTicketStore.TenantIdItemKey] = tenantId.ToString();
        var ticket = new AuthenticationTicket(principal, properties, "TenantScheme");

        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>())
                .Returns(tenantExists ? new Tenant { Id = tenantId, Slug = "acme", Status = status } : null);

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.StoreAsync(ticket, null, CancellationToken.None));

        await _tenantSwitcher.DidNotReceiveWithAnyArgs().SwitchToTenantAsync(default!, default);
        await _sessions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(TenantStatus.Provisioning)]
    [InlineData(TenantStatus.Trial)]
    [InlineData(TenantStatus.Active)]
    public async Task StoreAsync_TenantNotDead_CreatesSession(TenantStatus status)
    {
        // Invite-accept and password-reset flows also funnel through StoreAsync, and legitimately
        // create a session while the tenant is still Provisioning (before admin confirmation flips
        // it to Trial) - StoreAsync must not reject those, only tenants that are actually dead.
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));
        var properties = new AuthenticationProperties();
        properties.Items[TenantDatabaseTicketStore.TenantIdItemKey] = tenantId.ToString();
        var ticket = new AuthenticationTicket(principal, properties, "TenantScheme");

        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>())
                .Returns(new Tenant { Id = tenantId, Slug = "acme", Status = status });
        _sessions.AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var rawKey = await sut.StoreAsync(ticket, null, CancellationToken.None);

        Assert.NotNull(rawKey);
        await _sessions.Received(1).AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
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

        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(ActiveTenant(tenantId));

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

        await _tenantSwitcher.Received(1).SwitchToTenantAsync(
            Arg.Is<TenantRegistryEntry>(t => t.TenantId == tenantId), Arg.Any<CancellationToken>());
        Received.InOrder(async () =>
        {
            await _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _tenantSwitcher.SwitchToTenantAsync(Arg.Any<TenantRegistryEntry>(), Arg.Any<CancellationToken>());
            await _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
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
        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
    public async Task RetrieveAsync_TenantMissing_ReturnsNull_DoesNotSwitchOrReadUser()
    {
        var tenantId = Guid.NewGuid();
        var session = new Session
        {
            TenantId = tenantId,
            IsRevoked = false,
            StartedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(10)
        };
        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var ticket = await CreateSut().RetrieveAsync("some-raw-key", null, CancellationToken.None);

        Assert.Null(ticket);
        await _tenantSwitcher.DidNotReceiveWithAnyArgs().SwitchToTenantAsync(default!, default);
        await _users.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task RenewAsync_ExtendsExpiry_ReissuesCsrfCookie()
    {
        var tenantId = Guid.NewGuid();
        var session = new Session
        {
            IsRevoked = false,
            StartedAt = FixedNow.AddMinutes(-10),
            ExpiresAt = FixedNow.AddMinutes(5),
            CsrfTokenHash = "hash"
        };
        _sessions.GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(session);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(ActiveTenant(tenantId));

        var httpContext = new DefaultHttpContext();
        var env = Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var mockSp = Substitute.For<IServiceProvider>();
        mockSp.GetService(typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment)).Returns(env);
        httpContext.RequestServices = mockSp;
        httpContext.Request.Headers["Cookie"] = "onevo_csrf=existing_csrf_val";

        var sut = CreateSut();
        var properties = new AuthenticationProperties();
        properties.Items[TenantDatabaseTicketStore.TenantIdItemKey] = tenantId.ToString();
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(), properties, "TenantScheme");

        await sut.RenewAsync("some-raw-key", ticket, httpContext, CancellationToken.None);

        Assert.Equal(FixedNow.Add(SessionPolicy.SlidingWindow), session.ExpiresAt);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _tenantSwitcher.Received(1).SwitchToTenantAsync(
            Arg.Is<TenantRegistryEntry>(t => t.TenantId == tenantId), Arg.Any<CancellationToken>());

        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("onevo_csrf=existing_csrf_val", setCookieHeader);
        Assert.Contains("expires=", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenewAsync_TicketMissingTenantId_DoesNotRenew()
    {
        var sut = CreateSut();
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(), new AuthenticationProperties(), "TenantScheme");

        await sut.RenewAsync("some-raw-key", ticket, null, CancellationToken.None);

        await _tenantSwitcher.DidNotReceiveWithAnyArgs().SwitchToTenantAsync(default!, default);
        await _sessions.DidNotReceiveWithAnyArgs().GetByKeyHashAsync(default!, default);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task RemoveAsync_RevokesSession()
    {
        var tenantId = Guid.NewGuid();
        var session = new Session { TenantId = tenantId, IsRevoked = false };
        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(ActiveTenant(tenantId));

        var sut = CreateSut();
        await sut.RemoveAsync("some-raw-key", null, CancellationToken.None);

        Assert.True(session.IsRevoked);
        await _tenantSwitcher.Received(1).SwitchToTenantAsync(
            Arg.Is<TenantRegistryEntry>(t => t.TenantId == tenantId), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_SessionNotFound_DoesNotSwitchOrSave()
    {
        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);

        var sut = CreateSut();
        await sut.RemoveAsync("some-raw-key", null, CancellationToken.None);

        await _tenantSwitcher.DidNotReceiveWithAnyArgs().SwitchToTenantAsync(default!, default);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
