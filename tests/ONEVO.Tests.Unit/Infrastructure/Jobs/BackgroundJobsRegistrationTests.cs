using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Configuration;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Jobs;

/// <summary>Task 13: BackgroundJobs:Enabled=false removes every recurring job and keeps start-up checks and seeders.</summary>
public class BackgroundJobsRegistrationTests
{
    private static List<Type> HostedServices(bool? enabled)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=x;User Id=crownus_app;Password=x",
            ["Encryption:MasterKey"] = "unit-test-master-key-0123456789-abcdefgh",
        };
        if (enabled is { } value)
            settings[BackgroundJobsRegistration.EnabledKey] = value ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType!)
            .ToList();
    }

    private static bool IsRecurringJob(Type type) => typeof(BackgroundService).IsAssignableFrom(type);

    [Fact]
    public void Default_IsEnabled_AndRegistersEveryJob()
    {
        var jobs = HostedServices(null).Where(IsRecurringJob).ToList();

        jobs.Should().HaveCount(22);
        jobs.Select(t => t.Name).Should().Contain(["OutboxProcessor", "LeaveCreditJob", "BulkOnboardingBatchProcessor"]);
    }

    [Fact]
    public void Disabled_RegistersNoRecurringJob_ButKeepsStartupChecksAndSeeders()
    {
        var hosted = HostedServices(false);

        hosted.Where(IsRecurringJob).Should().BeEmpty();
        hosted.Select(t => t.Name).Should().Contain(
            ["ConfigurationStartupValidator", "MeetingsStartupCheck", "PermissionSeeder", "LookupDataSeeder"]);
        hosted.Should().HaveCount(HostedServices(true).Count - 22);
    }
}
