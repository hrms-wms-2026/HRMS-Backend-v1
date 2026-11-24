using FluentAssertions;
using ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.RateLimiting;

/// <summary>Improvements task 49: the in-memory rolling 429 counter.</summary>
public sealed class RateLimitRejectionStatsTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 10, 0, 30, TimeSpan.Zero));

    [Fact]
    public void Counts_per_tenant_and_kind_with_no_tenant_as_null()
    {
        var stats = new RateLimitRejectionStats(_time);

        stats.Record(TenantA, "User");
        stats.Record(TenantA, "User");
        stats.Record(TenantA, "TrayDevice");
        stats.Record(TenantB, "User");
        stats.Record(null, "Anonymous");
        stats.Record(null, "PreAuthentication");

        var counts = stats.Read(60).Counts;

        counts.Should().BeEquivalentTo(new[]
        {
            new { TenantId = (Guid?)TenantA, Kind = "User", Count = 2L },
            new { TenantId = (Guid?)TenantA, Kind = "TrayDevice", Count = 1L },
            new { TenantId = (Guid?)TenantB, Kind = "User", Count = 1L },
            new { TenantId = (Guid?)null, Kind = "Anonymous", Count = 1L },
            new { TenantId = (Guid?)null, Kind = "PreAuthentication", Count = 1L },
        });
    }

    [Fact]
    public void Read_keeps_only_the_asked_minutes()
    {
        var stats = new RateLimitRejectionStats(_time);
        stats.Record(TenantA, "User");
        _time.Advance(TimeSpan.FromMinutes(10));
        stats.Record(TenantA, "User");

        stats.Read(5).Counts.Should().ContainSingle().Which.Count.Should().Be(1);
        stats.Read(11).Counts.Should().ContainSingle().Which.Count.Should().Be(2);
    }

    [Fact]
    public void Buckets_older_than_60_minutes_are_dropped()
    {
        var stats = new RateLimitRejectionStats(_time);
        stats.Record(TenantA, "User");

        _time.Advance(TimeSpan.FromMinutes(60));

        stats.Read(60).Counts.Should().BeEmpty("the only rejection is 60 minutes old");
    }

    [Fact]
    public void A_reused_bucket_starts_from_zero()
    {
        var stats = new RateLimitRejectionStats(_time);
        stats.Record(TenantA, "User");
        stats.Record(TenantA, "User");

        // Exactly 60 minutes later the same slot in the ring comes round again.
        _time.Advance(TimeSpan.FromMinutes(60));
        stats.Record(TenantA, "User");

        stats.Read(60).Counts.Should().ContainSingle().Which.Count.Should().Be(1);
    }

    [Fact]
    public void Never_holds_more_tenants_than_the_cap_and_drops_the_oldest()
    {
        var stats = new RateLimitRejectionStats(_time, maxTenants: 3);
        var tenants = Enumerable.Range(1, 5).Select(i => new Guid($"00000000-0000-0000-0000-00000000000{i}")).ToArray();

        stats.Record(tenants[0], "User");
        stats.Record(tenants[1], "User");
        stats.Record(tenants[2], "User");
        stats.Record(tenants[0], "User"); // tenant 1 is now the most recent
        stats.Record(tenants[3], "User"); // drops tenant 2, the oldest
        stats.Record(tenants[4], "User"); // drops tenant 3

        stats.TrackedTenantCount.Should().Be(3);
        stats.Read(60).Counts.Select(c => c.TenantId).Should()
            .BeEquivalentTo(new Guid?[] { tenants[0], tenants[3], tenants[4] });
        stats.Read(60).Counts.Single(c => c.TenantId == tenants[0]).Count.Should().Be(2);
    }

    [Fact]
    public void A_flood_of_new_tenants_stays_at_the_default_cap()
    {
        var stats = new RateLimitRejectionStats(_time);

        for (var i = 0; i < RateLimitRejectionStats.DefaultMaxTenants + 1_000; i++)
            stats.Record(Guid.NewGuid(), "User");

        stats.TrackedTenantCount.Should().Be(RateLimitRejectionStats.DefaultMaxTenants);
        stats.Read(60).Counts.Should().HaveCount(RateLimitRejectionStats.DefaultMaxTenants);
    }

    [Fact]
    public async Task Parallel_records_are_all_counted()
    {
        var stats = new RateLimitRejectionStats(_time);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1_000; i++)
                stats.Record(TenantA, "User");
        })));

        stats.Read(60).Counts.Should().ContainSingle().Which.Count.Should().Be(8_000);
    }

    [Fact]
    public void Snapshot_says_when_counting_started()
    {
        var started = _time.GetUtcNow();
        var stats = new RateLimitRejectionStats(_time);
        _time.Advance(TimeSpan.FromMinutes(3));

        var snapshot = stats.Read(60);

        snapshot.CountingSince.Should().Be(started);
        snapshot.GeneratedAt.Should().Be(started.AddMinutes(3));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
