using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Identity.Sessions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>Improvement task 03, admin side: same cache contract as the tenant ticket store.</summary>
public sealed class AdminDatabaseTicketStoreCacheTests : IDisposable
{
    private const string RawKey = "raw-admin-key";

    private DateTimeOffset _now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IPlatformUserSessionRepository _sessions = Substitute.For<IPlatformUserSessionRepository>();
    private readonly IPlatformPermissionResolver _resolver = Substitute.For<IPlatformPermissionResolver>();
    private readonly IPlatformAuthEventRepository _authEvents = Substitute.For<IPlatformAuthEventRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());

    private readonly Guid _adminId = Guid.NewGuid();
    private readonly PlatformUserSession _session;
    private PlatformAccessProfile _profile;

    public AdminDatabaseTicketStoreCacheTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(IPlatformUserSessionRepository)).Returns(_sessions);
        _serviceProvider.GetService(typeof(IPlatformPermissionResolver)).Returns(_resolver);
        _serviceProvider.GetService(typeof(IPlatformAuthEventRepository)).Returns(_authEvents);
        _serviceProvider.GetService(typeof(IUnitOfWork)).Returns(_uow);
        _clock.UtcNow.Returns(_ => _now);

        _session = new PlatformUserSession
        {
            Id = Guid.NewGuid(),
            AccountId = _adminId,
            TokenHash = AdminDatabaseTicketStore.HashKey(RawKey),
            CreatedAt = _now.AddMinutes(-10),
            LastActivityAt = _now.AddMinutes(-10),
            ExpiresAt = _now.AddDays(1),
            CsrfTokenHash = "csrf-hash",
        };
        _sessions.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => _session);
        _sessions.When(s => s.RevokeByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => _session.RevokedAt = _now);

        _profile = Profile(PlatformPermissionCatalog.TenantsRead, PlatformPermissionCatalog.TenantsManage);
        _resolver.ResolveActiveUserAsync(_adminId, Arg.Any<CancellationToken>()).Returns(_ => _profile);
    }

    public void Dispose() => _memoryCache.Dispose();

    private PlatformAccessProfile Profile(params string[] codes)
    {
        var profile = new PlatformAccessProfile
        {
            UserId = _adminId,
            Email = "admin@onevo.com",
            Status = PlatformUser.StatusActive,
            MfaEnrolled = true,
            RoleNames = { "Platform Super Admin" },
        };
        foreach (var code in codes)
            profile.PermissionCodes.Add(code);
        return profile;
    }

    private SessionTicketCache CreateCache(int lifetimeSeconds = 30, int renewIntervalSeconds = 60) =>
        new(_memoryCache, _clock, Options.Create(new SessionTicketCacheOptions
        {
            LifetimeSeconds = lifetimeSeconds,
            InvalidationGraceSeconds = 2,
            RenewWriteIntervalSeconds = renewIntervalSeconds,
        }));

    private AdminDatabaseTicketStore CreateSut(SessionTicketCache cache) => new(_scopeFactory, _clock, cache);

    private Task<AuthenticationTicket?> RetrieveAsync(AdminDatabaseTicketStore sut) =>
        sut.RetrieveAsync(RawKey, null, CancellationToken.None);

    private Task SessionReads(int expected) =>
        _sessions.Received(expected).GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

    private static HashSet<string> Permissions(AuthenticationTicket ticket) => ticket.Principal.Claims
        .Where(c => c.Type == PlatformPermissionCatalog.PermissionClaimType)
        .Select(c => c.Value)
        .ToHashSet();

    [Fact]
    public async Task RepeatRequestsInsideWindow_DoZeroSessionDatabaseReads()
    {
        var sut = CreateSut(CreateCache());

        var first = await RetrieveAsync(sut);
        _now = _now.AddSeconds(10);
        var second = await RetrieveAsync(sut);

        Assert.NotNull(first);
        Assert.NotNull(second);
        await SessionReads(1);
        await _resolver.Received(1).ResolveActiveUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(
            first!.Principal.Claims.Select(c => (c.Type, c.Value)),
            second!.Principal.Claims.Select(c => (c.Type, c.Value)));
    }

    [Fact]
    public async Task LifetimeZero_DisablesTheCache()
    {
        var sut = CreateSut(CreateCache(lifetimeSeconds: 0));

        await RetrieveAsync(sut);
        await RetrieveAsync(sut);

        await SessionReads(2);
    }

    [Fact]
    public async Task Logout_RejectsTheSessionOnTheNextRequest()
    {
        var sut = CreateSut(CreateCache());
        Assert.NotNull(await RetrieveAsync(sut));

        await sut.RemoveAsync(RawKey, null, CancellationToken.None);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task RevokeAllSessionsOfAdmin_RejectsTheSessionOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        Assert.NotNull(await RetrieveAsync(sut));

        _session.RevokedAt = _now;
        cache.EvictUser(_adminId);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task PermissionRemoval_TakesEffectOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        Assert.Contains(PlatformPermissionCatalog.TenantsManage, Permissions((await RetrieveAsync(sut))!));

        _profile = Profile(PlatformPermissionCatalog.TenantsRead);
        cache.EvictTenant(SessionTicketCacheScopes.Platform);

        Assert.DoesNotContain(PlatformPermissionCatalog.TenantsManage, Permissions((await RetrieveAsync(sut))!));
    }

    [Fact]
    public async Task ExpiredSession_IsRejectedEvenWhileCached()
    {
        _session.ExpiresAt = _now.AddSeconds(10);
        var sut = CreateSut(CreateCache());
        Assert.NotNull(await RetrieveAsync(sut));

        _now = _now.AddSeconds(11);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task AbsoluteLifetimeReached_IsRejectedEvenWhileCached()
    {
        _session.CreatedAt = _now - SessionPolicy.AbsoluteLifetime + TimeSpan.FromSeconds(5);
        var sut = CreateSut(CreateCache());
        Assert.NotNull(await RetrieveAsync(sut));

        _now = _now.AddSeconds(6);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task DeactivatedAdmin_IsRejectedOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        Assert.NotNull(await RetrieveAsync(sut));

        // A non-active PlatformUser.Status makes the resolver return null; the tracked status
        // change is turned into EvictUser by the EF eviction interceptor.
        _resolver.ResolveActiveUserAsync(_adminId, Arg.Any<CancellationToken>()).Returns((PlatformAccessProfile?)null);
        cache.EvictUser(_adminId);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task RenewedSession_PastItsOriginalExpiry_IsStillAcceptedFromCache()
    {
        _session.ExpiresAt = _now.AddSeconds(10);
        var sut = CreateSut(CreateCache());
        var ticket = (await RetrieveAsync(sut))!;

        var renewedExpiry = _now.AddDays(30);
        ticket.Properties.ExpiresUtc = renewedExpiry;
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);

        _now = _now.AddSeconds(20);
        var after = await RetrieveAsync(sut);

        Assert.NotNull(after);
        Assert.Equal(renewedExpiry, after!.Properties.ExpiresUtc);
        await SessionReads(2); // one retrieve + the renew's own session read
    }

    [Fact]
    public async Task ThrottledRenew_StillSlidesTheCachedExpiry()
    {
        var sut = CreateSut(CreateCache(renewIntervalSeconds: 60));
        var ticket = (await RetrieveAsync(sut))!;
        ticket.Properties.ExpiresUtc = _now.AddDays(30);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);

        _now = _now.AddSeconds(10);
        var slid = _now.AddDays(30);
        ticket.Properties.ExpiresUtc = slid;
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);

        var after = await RetrieveAsync(sut);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(slid, after!.Properties.ExpiresUtc);
    }

    [Fact]
    public async Task RenewedSession_PastTheAbsoluteLifetime_IsRejectedFromCache()
    {
        var absoluteCap = _now.AddSeconds(15);
        _session.CreatedAt = absoluteCap - SessionPolicy.AbsoluteLifetime;
        _session.ExpiresAt = _now.AddSeconds(10);
        var sut = CreateSut(CreateCache());
        var ticket = (await RetrieveAsync(sut))!;

        ticket.Properties.ExpiresUtc = _now.AddDays(30);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);

        _now = _now.AddSeconds(12);
        Assert.NotNull(await RetrieveAsync(sut));

        _now = _now.AddSeconds(4);
        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task Renew_WritesLastActivityAtAtMostOncePerInterval()
    {
        var sut = CreateSut(CreateCache(renewIntervalSeconds: 60));
        var ticket = (await RetrieveAsync(sut))!;

        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        _now = _now.AddSeconds(30);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        _now = _now.AddSeconds(31);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(_now, _session.LastActivityAt);
    }
}
