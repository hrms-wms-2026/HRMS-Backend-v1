using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.Leave;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>Task 13: two API instances (two lease services with different instance ids on one SQL Server database)
/// never run the same job twice at once, and a daily job runs once per day across both.</summary>
public sealed class ExclusiveJobRunnerIntegrationTests
{
    private static async Task<(ExclusiveJobRunner A, ExclusiveJobRunner B)> CreateAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        var scopes = new ServiceCollection()
            .AddScoped<ApplicationDbContext>(_ => h.NewContext())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return (
            new ExclusiveJobRunner(new JobLeaseService(scopes, "instance-a"), NullLogger<ExclusiveJobRunner>.Instance),
            new ExclusiveJobRunner(new JobLeaseService(scopes, "instance-b"), NullLogger<ExclusiveJobRunner>.Instance));
    }

    [Fact]
    public async Task TwoInstances_StartedTogether_RunTheWorkOnce()
    {
        var (a, b) = await CreateAsync();
        var schedule = new JobSchedule("test.together", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        var runs = 0;
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var first = a.RunAsync(schedule, async _ =>
        {
            Interlocked.Increment(ref runs);
            started.SetResult();
            await release.Task;
        }, CancellationToken.None);
        await started.Task;

        var second = await b.RunAsync(schedule, _ =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }, CancellationToken.None);
        release.SetResult();

        Assert.Equal(JobRunOutcome.SkippedHeldElsewhere, second);
        Assert.Equal(JobRunOutcome.Completed, await first);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task LeaveCredit_TwoInstancesOnTheSameDay_CreditOnce()
    {
        // The job's own 01:00 check fires on both instances within the hour; the daily lease decides which credits.
        var (a, b) = await CreateAsync();
        var credits = 0;
        Task Credit(CancellationToken _) { Interlocked.Increment(ref credits); return Task.CompletedTask; }

        Assert.Equal(JobRunOutcome.Completed, await a.RunAsync(LeaveCreditJob.Schedule, Credit, CancellationToken.None));
        Assert.Equal(JobRunOutcome.SkippedHeldElsewhere, await b.RunAsync(LeaveCreditJob.Schedule, Credit, CancellationToken.None));
        Assert.Equal(JobRunOutcome.SkippedHeldElsewhere, await b.RunAsync(LeaveCreditJob.Schedule, Credit, CancellationToken.None));

        Assert.Equal(1, credits);
    }

    [Fact]
    public async Task NoGap_ReleasesAfterTheRun_SoTheOtherInstanceCanTakeTheNextOne()
    {
        var (a, b) = await CreateAsync();
        var schedule = new JobSchedule("test.no-gap", TimeSpan.FromMinutes(1), TimeSpan.Zero);

        Assert.Equal(JobRunOutcome.Completed, await a.RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None));
        Assert.Equal(JobRunOutcome.Completed, await b.RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task FailedRun_ReleasesTheLease()
    {
        var (a, b) = await CreateAsync();
        var schedule = JobSchedule.Daily("test.failed", TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            a.RunAsync(schedule, _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Equal(JobRunOutcome.Completed, await b.RunAsync(schedule, _ => Task.CompletedTask, CancellationToken.None));
    }
}
