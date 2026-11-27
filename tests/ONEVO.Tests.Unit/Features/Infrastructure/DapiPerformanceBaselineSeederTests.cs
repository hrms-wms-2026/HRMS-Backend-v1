using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>Task 06 Part B: the baseline seed adds hundreds of people, so it must never run by accident.</summary>
public sealed class DapiPerformanceBaselineSeederTests
{
    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Development", null, false)]
    [InlineData("Test", "true", false)]
    [InlineData("Staging", "true", false)]
    [InlineData("Production", "true", false)]
    public void Runs_only_in_Development_with_the_setting_on(string environment, string? setting, bool expected)
    {
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);
        var values = new Dictionary<string, string?>();
        if (setting is not null)
            values[DapiPerformanceBaselineSeeder.ConfigKey] = setting;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        DapiPerformanceBaselineSeeder.IsEnabled(host.Object, configuration).Should().Be(expected);
    }
}
