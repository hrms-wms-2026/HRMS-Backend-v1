using FluentAssertions;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

/// <summary>
/// The demo history must stop before today (Colombo time). A seeded record for today makes the
/// tray agent report the day as already clocked in and out, so a real employee lands on
/// "Workday Completed" without ever clocking in.
/// </summary>
public sealed class DapiHistorySeederCutoffTests
{
    private static readonly TimeSpan Colombo = TimeSpan.FromMinutes(330);

    [Fact]
    public void Yesterday_is_seeded()
    {
        var now = new DateTimeOffset(2026, 10, 14, 10, 0, 0, Colombo);

        DapiHistorySeeder.IsHistoricalDate(new DateOnly(2026, 10, 13), now).Should().BeTrue();
    }

    [Fact]
    public void Today_is_not_seeded()
    {
        var now = new DateTimeOffset(2026, 10, 14, 10, 0, 0, Colombo);

        DapiHistorySeeder.IsHistoricalDate(new DateOnly(2026, 10, 14), now).Should().BeFalse();
    }

    [Fact]
    public void Future_days_are_not_seeded()
    {
        var now = new DateTimeOffset(2026, 10, 14, 10, 0, 0, Colombo);

        DapiHistorySeeder.IsHistoricalDate(new DateOnly(2026, 10, 20), now).Should().BeFalse();
    }

    [Fact]
    public void Today_is_resolved_in_Colombo_time_not_UTC()
    {
        // 2026-10-14 00:30 in Colombo is still 2026-10-13 in UTC.
        var now = new DateTimeOffset(2026, 10, 13, 19, 0, 0, TimeSpan.Zero);

        DapiHistorySeeder.IsHistoricalDate(new DateOnly(2026, 10, 13), now).Should().BeTrue();
        DapiHistorySeeder.IsHistoricalDate(new DateOnly(2026, 10, 14), now).Should().BeFalse();
    }
}
