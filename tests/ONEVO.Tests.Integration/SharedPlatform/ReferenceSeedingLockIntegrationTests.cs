using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>Task 13: a second instance starting together waits for the first instance's reference seeding.</summary>
public sealed class ReferenceSeedingLockIntegrationTests
{
    private static ReferenceSeedingLock NewLock(string connectionString)
        => new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
                .Build(),
            NullLogger<ReferenceSeedingLock>.Instance);

    [Fact]
    public async Task SecondInstance_WaitsUntilTheFirstHasReleased()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var first = NewLock(h.ConnectionString);
        await using var second = NewLock(h.ConnectionString);

        await first.AcquireAsync(CancellationToken.None);
        var waiting = second.AcquireAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(waiting.IsCompleted, "the second instance must wait while the first is seeding");

        await first.ReleaseAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await second.ReleaseAsync();
    }

    [Fact]
    public async Task ADeadInstance_FreesTheLock()
    {
        var h = await MyTeamDb.CreateAsync();
        var crashed = NewLock(h.ConnectionString);
        await using var next = NewLock(h.ConnectionString);

        await crashed.AcquireAsync(CancellationToken.None);
        await crashed.DisposeAsync(); // connection closed without releasing, as when a process dies

        await next.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await next.ReleaseAsync();
    }
}
