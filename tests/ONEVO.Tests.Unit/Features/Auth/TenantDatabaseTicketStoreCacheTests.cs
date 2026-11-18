using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using ONEVO.Infrastructure.Security;
using Xunit;
using CoreHrEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Improvement task 03: the tenant ticket store caches the built ticket data for a short window,
/// keyed by the session key hash. These tests pin the security contract of that cache: a revoked,
/// expired or de-permissioned session is never served from it.
/// </summary>
public sealed class TenantDatabaseTicketStoreCacheTests : IDisposable
{
    private const string RawKey = "raw-session-key";

    private DateTimeOffset _now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

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
    private readonly CoreHrEmployeeRepository _employees = Substitute.For<CoreHrEmployeeRepository>();
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Session _session;

    public TenantDatabaseTicketStoreCacheTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(ISessionRepository)).Returns(_sessions);
        _serviceProvider.GetService(typeof(IUserRepository)).Returns(_users);
        _serviceProvider.GetService(typeof(IPermissionResolver)).Returns(_permissions);
        _serviceProvider.GetService(typeof(IUnitOfWork)).Returns(_uow);
        _serviceProvider.GetService(typeof(ITenantRepository)).Returns(_tenants);
        _serviceProvider.GetService(typeof(ITenantContextSwitcher)).Returns(_tenantSwitcher);
        _serviceProvider.GetService(typeof(CoreHrEmployeeRepository)).Returns(_employees);
        _clock.UtcNow.Returns(_ => _now);

        _session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            TenantId = _tenantId,
            StartedAt = _now.AddMinutes(-10),
            LastActivityAt = _now.AddMinutes(-10),
            ExpiresAt = _now.AddDays(1),
            CsrfTokenHash = "csrf-hash",
        };
        _sessions.GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => _session);
        _sessions.GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => _session);
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = _tenantId, Slug = "acme", Status = TenantStatus.Active });
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = _userId, Email = "a@b.com", IsActive = true });
        _permissions.ResolveAsync(_userId, _tenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(["employees:read", "employees:write"]);
    }

    public void Dispose() => _memoryCache.Dispose();

    private SessionTicketCache CreateCache(int lifetimeSeconds = 30, int renewIntervalSeconds = 60) =>
        new(_memoryCache, _clock, Options.Create(new SessionTicketCacheOptions
        {
            LifetimeSeconds = lifetimeSeconds,
            InvalidationGraceSeconds = 2,
            RenewWriteIntervalSeconds = renewIntervalSeconds,
        }));

    private TenantDatabaseTicketStore CreateSut(SessionTicketCache cache) =>
        new(_scopeFactory, _clock, cache, NullLogger<TenantDatabaseTicketStore>.Instance);

    private Task<AuthenticationTicket?> RetrieveAsync(TenantDatabaseTicketStore sut) =>
        sut.RetrieveAsync(RawKey, null, CancellationToken.None);

    private Task SessionReads(int expected) =>
        _sessions.Received(expected).GetByKeyHashForTenantResolutionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task RepeatRequestsInsideWindow_DoZeroSessionDatabaseReads()
    {
        var sut = CreateSut(CreateCache());

        var first = await RetrieveAsync(sut);
        _now = _now.AddSeconds(10);
        var second = await RetrieveAsync(sut);
        var third = await RetrieveAsync(sut);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        await SessionReads(1);
        await _users.Received(1).GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _tenants.Received(1).GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _permissions.Received(1).ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        Assert.Equal(
            first!.Principal.Claims.Select(c => (c.Type, c.Value)),
            third!.Principal.Claims.Select(c => (c.Type, c.Value)));
        Assert.Equal(_tenantId.ToString(), third.Properties.Items[TenantDatabaseTicketStore.TenantIdItemKey]);
    }

    [Fact]
    public async Task CachedTicket_IsAFreshInstanceEachTime()
    {
        var sut = CreateSut(CreateCache());

        var first = await RetrieveAsync(sut);
        first!.Properties.ExpiresUtc = _now.AddYears(5);
        var second = await RetrieveAsync(sut);

        Assert.NotSame(first, second);
        Assert.Equal(_session.ExpiresAt, second!.Properties.ExpiresUtc);
    }

    [Fact]
    public async Task EntryOlderThanLifetime_IsReloaded()
    {
        var sut = CreateSut(CreateCache(lifetimeSeconds: 30));

        await RetrieveAsync(sut);
        _now = _now.AddSeconds(31);
        await RetrieveAsync(sut);

        await SessionReads(2);
    }

    [Fact]
    public async Task LifetimeZero_DisablesTheCache()
    {
        var sut = CreateSut(CreateCache(lifetimeSeconds: 0));

        await RetrieveAsync(sut);
        await RetrieveAsync(sut);
        await RetrieveAsync(sut);

        await SessionReads(3);
    }

    [Fact]
    public async Task Logout_RejectsTheSessionOnTheNextRequest()
    {
        var sut = CreateSut(CreateCache());
        Assert.NotNull(await RetrieveAsync(sut));

        await sut.RemoveAsync(RawKey, null, CancellationToken.None);

        Assert.True(_session.IsRevoked);
        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task AdminRevokesSession_RejectsItOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        Assert.NotNull(await RetrieveAsync(sut));

        // What RevokeTenantSession / RevokeUserAccountSession do: flag the row, which the EF
        // eviction interceptor (or the repository, for bulk updates) turns into a cache eviction.
        _session.IsRevoked = true;
        cache.EvictUser(_userId);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task PermissionRemoval_TakesEffectOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        var permissionVersions = new PermissionVersionService(cache);

        var before = await RetrieveAsync(sut);
        Assert.Contains(before!.Principal.FindAll("permission"), c => c.Value == "employees:write");

        _permissions.ResolveAsync(_userId, _tenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(["employees:read"]);
        await permissionVersions.IncrementVersionAsync(_userId);

        var after = await RetrieveAsync(sut);
        Assert.DoesNotContain(after!.Principal.FindAll("permission"), c => c.Value == "employees:write");
    }

    [Fact]
    public async Task RolePermissionChangeForTenant_TakesEffectOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        await RetrieveAsync(sut);

        _permissions.ResolveAsync(_userId, _tenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(["employees:read"]);
        cache.EvictTenant(_tenantId);

        var after = await RetrieveAsync(sut);
        Assert.DoesNotContain(after!.Principal.FindAll("permission"), c => c.Value == "employees:write");
    }

    [Fact]
    public async Task LegalEntitySwitch_TakesEffectOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        await RetrieveAsync(sut);

        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        _session.ActiveEmployeeId = employeeId;
        _employees.GetLegalEntityIdAsync(_tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(legalEntityId);
        cache.EvictSession(TenantDatabaseTicketStore.HashKey(RawKey));

        var after = await RetrieveAsync(sut);
        Assert.Equal(legalEntityId.ToString(), after!.Principal.FindFirstValue("legal_entity_id"));
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
        _session.StartedAt = _now - SessionPolicy.AbsoluteLifetime + TimeSpan.FromSeconds(5);
        var sut = CreateSut(CreateCache());
        Assert.NotNull(await RetrieveAsync(sut));

        _now = _now.AddSeconds(6);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task RebuildInsideGraceWindowAfterEviction_IsNotCached()
    {
        // A request that read the rows just before a revoke committed must not put them back.
        var cache = CreateCache();
        var sut = CreateSut(cache);
        await RetrieveAsync(sut);

        cache.EvictUser(_userId);
        await RetrieveAsync(sut);
        await RetrieveAsync(sut);
        await SessionReads(3);

        _now = _now.AddSeconds(3);
        await RetrieveAsync(sut);
        await RetrieveAsync(sut);
        await SessionReads(4);
    }

    [Fact]
    public async Task EvictingAnotherUser_KeepsThisEntry()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        await RetrieveAsync(sut);

        cache.EvictUser(Guid.NewGuid());
        cache.EvictTenant(Guid.NewGuid());
        await RetrieveAsync(sut);

        await SessionReads(1);
    }

    [Fact]
    public async Task EvictAll_DropsEveryEntry()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        await RetrieveAsync(sut);

        cache.EvictAll();
        await RetrieveAsync(sut);

        await SessionReads(2);
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
        await _sessions.Received(1).GetByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        _now = _now.AddSeconds(31);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(_now, _session.LastActivityAt);
    }

    [Fact]
    public async Task Renew_UpdatesTheCachedExpirySoTheNewExpiryIsServed()
    {
        var sut = CreateSut(CreateCache());
        var ticket = (await RetrieveAsync(sut))!;
        var newExpiry = _now.AddDays(30);
        ticket.Properties.ExpiresUtc = newExpiry;

        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        var after = await RetrieveAsync(sut);

        Assert.Equal(newExpiry, after!.Properties.ExpiresUtc);
        Assert.Equal(newExpiry.ToString("O"), after.Principal.FindFirstValue("session_expires_at"));
        await SessionReads(1);
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

        _now = _now.AddSeconds(20); // past the original expiry, inside the renewed one
        var after = await RetrieveAsync(sut);

        Assert.NotNull(after);
        Assert.Equal(renewedExpiry, after!.Properties.ExpiresUtc);
        await SessionReads(1);
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
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None); // throttled: no DB write

        var after = await RetrieveAsync(sut);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(slid, after!.Properties.ExpiresUtc);
        Assert.Equal(slid.ToString("O"), after.Principal.FindFirstValue("session_expires_at"));
    }

    [Fact]
    public async Task RenewedSession_PastTheAbsoluteLifetime_IsRejectedFromCache()
    {
        var absoluteCap = _now.AddSeconds(15);
        _session.StartedAt = absoluteCap - SessionPolicy.AbsoluteLifetime;
        _session.ExpiresAt = _now.AddSeconds(10);
        var sut = CreateSut(CreateCache());
        var ticket = (await RetrieveAsync(sut))!;

        ticket.Properties.ExpiresUtc = _now.AddDays(30);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        Assert.Equal(absoluteCap, _session.ExpiresAt);

        _now = _now.AddSeconds(12);
        Assert.NotNull(await RetrieveAsync(sut));

        _now = _now.AddSeconds(4); // past the absolute lifetime
        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task FailedRenewWrite_DoesNotSuppressTheNextAttempt()
    {
        var sut = CreateSut(CreateCache(renewIntervalSeconds: 60));
        var ticket = (await RetrieveAsync(sut))!;
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("db down"), _ => Task.FromResult(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RenewAsync(RawKey, ticket, null, CancellationToken.None));
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);

        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivatedUser_IsRejectedOnTheNextRequest()
    {
        var cache = CreateCache();
        var sut = CreateSut(cache);
        Assert.NotNull(await RetrieveAsync(sut));

        // SuspendUserAccount / CompleteOffboarding set User.IsActive = false (tracked), which the
        // EF eviction interceptor turns into EvictUser (see SessionTicketCacheInvalidationTests).
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = _userId, Email = "a@b.com", IsActive = false });
        cache.EvictUser(_userId);

        Assert.Null(await RetrieveAsync(sut));
    }

    [Fact]
    public async Task RenewIntervalZero_WritesEveryTime()
    {
        var sut = CreateSut(CreateCache(renewIntervalSeconds: 0));
        var ticket = (await RetrieveAsync(sut))!;

        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);
        await sut.RenewAsync(RawKey, ticket, null, CancellationToken.None);

        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
