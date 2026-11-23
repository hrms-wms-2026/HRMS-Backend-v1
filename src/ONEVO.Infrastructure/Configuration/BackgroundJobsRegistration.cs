using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Task 13: <c>BackgroundJobs:Enabled</c> lets the same image run as a web instance (false: no recurring jobs) or as
/// an instance that also runs them (true, the default, so a single instance keeps working unchanged). Only recurring
/// jobs use <see cref="AddBackgroundJob{TJob}"/>; start-up checks and seeders always run. Which job is which:
/// <c>docs/deployment/background-jobs.md</c>.
/// </summary>
public static class BackgroundJobsRegistration
{
    public const string EnabledKey = "BackgroundJobs:Enabled";

    public static bool IsEnabled(IConfiguration configuration)
        => configuration.GetValue(EnabledKey, defaultValue: true);

    public static IServiceCollection AddBackgroundJob<TJob>(this IServiceCollection services, IConfiguration configuration)
        where TJob : class, IHostedService
    {
        if (IsEnabled(configuration))
            services.AddHostedService<TJob>();
        return services;
    }
}
