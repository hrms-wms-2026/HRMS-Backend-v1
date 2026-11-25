using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitRejections;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.RateLimits;

/// <summary>Improvements task 49: the admin rejections read.</summary>
public sealed class GetRateLimitRejectionsQueryHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly IRateLimitRejectionStats _stats = Substitute.For<IRateLimitRejectionStats>();
    private readonly IRateLimitSettingsProvider _settings = Substitute.For<IRateLimitSettingsProvider>();
    private readonly IRateLimitSettingRepository _repository = Substitute.For<IRateLimitSettingRepository>();

    private GetRateLimitRejectionsQueryHandler Handler() => new(_stats, _settings, _repository);

    private void Counts(params RateLimitRejectionCount[] counts) =>
        _stats.Read(Arg.Any<int>()).Returns(new RateLimitRejectionSnapshot(Now, Now.AddHours(-2), counts));

    [Fact]
    public async Task Orders_tenants_by_total_with_names_kinds_and_effective_limits()
    {
        Counts(
            new(TenantA, "User", 3),
            new(TenantB, "User", 4),
            new(TenantB, "TrayDevice", 6),
            new(null, "Anonymous", 2));
        _repository.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [TenantA] = "Acme", [TenantB] = "Globex" });
        var tenantB = new RateLimitSettings
        {
            SignedIn = new TokenBucketLimit { TokenLimit = 200, TokensPerPeriod = 20, ReplenishmentPeriodSeconds = 1 },
        };
        _settings.GetSettings(Arg.Any<Guid?>()).Returns(new RateLimitSettingsSnapshot(1, new RateLimitSettings()));
        _settings.GetSettings(TenantB).Returns(new RateLimitSettingsSnapshot(2, tenantB));

        var result = await Handler().Handle(new GetRateLimitRejectionsQuery(60), default);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Scope.Should().Be("instance");
        dto.Note.Should().Contain("this API server only");
        dto.Minutes.Should().Be(60);
        dto.CountingSince.Should().Be(Now.AddHours(-2));
        dto.TotalRejections.Should().Be(15);
        dto.Tenants.Select(t => t.TenantId).Should().Equal(TenantB, TenantA, null);

        var globex = dto.Tenants[0];
        globex.TenantName.Should().Be("Globex");
        globex.TotalRejections.Should().Be(10);
        globex.ByKind.Should().Equal(new RateLimitRejectionKindDto("TrayDevice", 6), new RateLimitRejectionKindDto("User", 4));
        globex.EffectiveLimits.Should().Equal(
            new RateLimitRejectionLimitDto("SignedIn", 1200, 200),
            new RateLimitRejectionLimitDto("TrayDevice", 300, 60),
            new RateLimitRejectionLimitDto("TenantCeiling", 12000, 3000));

        var none = dto.Tenants[2];
        none.TenantName.Should().BeNull();
        none.EffectiveLimits.Should().BeEmpty("requests with no tenant have no per-tenant limits");
    }

    [Fact]
    public async Task Keeps_only_the_top_50_tenants()
    {
        Counts(Enumerable.Range(1, 60)
            .Select(i => new RateLimitRejectionCount(Guid.NewGuid(), "User", i))
            .ToArray());
        _repository.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _settings.GetSettings(Arg.Any<Guid?>()).Returns(new RateLimitSettingsSnapshot(1, new RateLimitSettings()));

        var result = await Handler().Handle(new GetRateLimitRejectionsQuery(60), default);

        result.Value!.Tenants.Should().HaveCount(GetRateLimitRejectionsQuery.MaxTenants);
        result.Value.Tenants[0].TotalRejections.Should().Be(60);
        result.Value.Tenants[^1].TotalRejections.Should().Be(11);
        result.Value.TotalRejections.Should().Be(Enumerable.Range(1, 60).Sum(), "the total covers every tenant, not only the top 50");
        await _repository.Received(1).GetTenantNamesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 50), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_rejections_reads_no_tenant_names()
    {
        Counts();

        var result = await Handler().Handle(new GetRateLimitRejectionsQuery(15), default);

        result.Value!.Tenants.Should().BeEmpty();
        result.Value.TotalRejections.Should().Be(0);
        result.Value.Note.Should().Contain("last 15 minutes");
        _stats.Received(1).Read(15);
        await _repository.DidNotReceive().GetTenantNamesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Minutes_must_be_1_to_60(int minutes, bool valid)
    {
        new GetRateLimitRejectionsQueryValidator().Validate(new GetRateLimitRejectionsQuery(minutes))
            .IsValid.Should().Be(valid);
    }
}
