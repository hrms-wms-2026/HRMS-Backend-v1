using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>job_leases (meetings-as-tasks Phase 2 spec 4.1, SCL-01): one instance at a time runs a singleton job; an
/// expired lease is taken over and its old holder can no longer renew it.</summary>
public sealed class JobLeaseIntegrationTests
{
    private const string Job = "meetings.attendance-sync";

    private static async Task<(MyTeamDb H, JobLeaseService A, JobLeaseService B)> CreateAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        var scopes = new ServiceCollection()
            .AddScoped<ApplicationDbContext>(_ => h.NewContext())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return (h, new JobLeaseService(scopes, "instance-a"), new JobLeaseService(scopes, "instance-b"));
    }

    private static async Task<object?> ScalarAsync(MyTeamDb h, string sql)
    {
        await using var connection = new NpgsqlConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<DateTime> LeaseUntilAsync(MyTeamDb h)
        => (DateTime)(await ScalarAsync(h, $"SELECT lease_until FROM job_leases WHERE job_name = '{Job}'"))!;

    [Fact]
    public async Task FirstCaller_Acquires_SecondInstance_IsRefused()
    {
        var (h, a, b) = await CreateAsync();

        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.False(await b.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Equal("instance-a", await ScalarAsync(h, $"SELECT owner_instance_id FROM job_leases WHERE job_name = '{Job}'"));
    }

    [Fact]
    public async Task Holder_Renews()
    {
        var (h, a, _) = await CreateAsync();
        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        var first = await LeaseUntilAsync(h);

        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.True(await LeaseUntilAsync(h) > first);
    }

    [Fact]
    public async Task ExpiredLease_IsTakenOver_AndTheOldHolderCannotRenew()
    {
        var (h, a, b) = await CreateAsync();
        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromSeconds(1), CancellationToken.None));

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((bool)(await ScalarAsync(h, $"SELECT lease_until >= now() FROM job_leases WHERE job_name = '{Job}'"))!)
        {
            Assert.True(DateTime.UtcNow < deadline, "the 1 s lease never expired");
            await Task.Delay(200);
        }

        Assert.True(await b.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.False(await a.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task ALeaseTakenOverAndReleased_CannotBeRenewedByItsOldHolder()
    {
        var (h, a, b) = await CreateAsync();
        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromSeconds(1), CancellationToken.None));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((bool)(await ScalarAsync(h, $"SELECT lease_until >= now() FROM job_leases WHERE job_name = '{Job}'"))!)
        {
            Assert.True(DateTime.UtcNow < deadline, "the 1 s lease never expired");
            await Task.Delay(200);
        }
        Assert.True(await b.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        await b.ReleaseAsync(Job, CancellationToken.None);

        // A stalled past its lease, B ran and finished: A must learn it lost the lease, not silently carry on (spec 7).
        Assert.False(await a.RenewAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task RenewAsync_ByTheHolder_ExtendsTheLease()
    {
        var (h, a, b) = await CreateAsync();
        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        var first = await LeaseUntilAsync(h);

        Assert.True(await a.RenewAsync(Job, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await b.RenewAsync(Job, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.True(await LeaseUntilAsync(h) > first);
    }

    [Fact]
    public void InstanceId_FitsTheColumn_EvenWithALongHostName()
    {
        var id = JobLeaseService.BuildInstanceId(new string('k', 63), 4194304, Guid.NewGuid());

        Assert.True(id.Length <= 100, $"{id.Length} characters");
        Assert.StartsWith(new string('k', 40) + ":4194304:", id);
    }

    [Fact]
    public async Task Release_ByNonHolder_DoesNothing()
    {
        var (h, a, b) = await CreateAsync();
        Assert.True(await a.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
        var before = await LeaseUntilAsync(h);

        await b.ReleaseAsync(Job, CancellationToken.None);

        Assert.Equal(before, await LeaseUntilAsync(h));
        Assert.False(await b.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));

        await a.ReleaseAsync(Job, CancellationToken.None);
        Assert.True(await b.TryAcquireAsync(Job, TimeSpan.FromMinutes(1), CancellationToken.None));
    }
}
