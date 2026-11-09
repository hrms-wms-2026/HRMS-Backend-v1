using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

/// <summary>
/// In the Test environment a failed seed must stop the host, so a missing test tenant shows up as
/// a seeding error instead of an unrelated test failure later. A timeout is retried once first.
/// Development keeps logging and starting.
/// </summary>
public sealed class DevSmokeTestTenantSeederStartupTests
{
    [Fact]
    public async Task TestEnvironment_SeedFailure_StopsStartup()
    {
        var (seeder, attempts) = CreateSeeder("Test", () => new InvalidOperationException("seed broke"));

        var act = () => seeder.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*seed broke*");
        attempts().Should().Be(1);
    }

    [Fact]
    public async Task TestEnvironment_Timeout_IsRetriedOnceThenStopsStartup()
    {
        var (seeder, attempts) = CreateSeeder("Test", () => new TimeoutException("stalled"));

        var act = () => seeder.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        attempts().Should().Be(2);
    }

    [Fact]
    public async Task DevelopmentEnvironment_SeedFailure_IsLoggedAndStartupContinues()
    {
        var (seeder, attempts) = CreateSeeder("Development", () => new InvalidOperationException("seed broke"));

        await seeder.StartAsync(CancellationToken.None);

        attempts().Should().Be(1);
    }

    private static (DevSmokeTestTenantSeeder Seeder, Func<int> Attempts) CreateSeeder(
        string environment, Func<Exception> failure)
    {
        var attempts = 0;
        var services = new ServiceCollection();
        services.AddScoped<ApplicationDbContext>(_ =>
        {
            attempts++;
            throw failure();
        });
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);

        var seeder = new DevSmokeTestTenantSeeder(
            services.BuildServiceProvider(),
            new ConfigurationBuilder().Build(),
            host.Object,
            NullLogger<DevSmokeTestTenantSeeder>.Instance);
        return (seeder, () => attempts);
    }
}
