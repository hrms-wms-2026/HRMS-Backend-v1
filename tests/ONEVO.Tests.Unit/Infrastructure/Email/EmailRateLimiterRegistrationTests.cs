using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.ExternalServices.Email;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Email;

/// <summary>
/// The outbox processor resolves every <see cref="IOutboxMessageHandler"/> in one call, so a missing
/// <see cref="IEmailRateLimiter"/> would stop every calendar invite email. The limiter must also be a
/// singleton: the pacing window is per instance, and a scoped limiter would reset on every scope.
/// </summary>
public sealed class EmailRateLimiterRegistrationTests
{
    private static IServiceCollection Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=di_registration_test_only",
                ["Encryption:MasterKey"] = "email_rate_limiter_registration_test_key"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        // The API host adds this in Program.cs; without it the transitive graph of the email service
        // (the cache-backed ICacheService) cannot be built.
        services.AddMemoryCache();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services;
    }

    [Fact]
    public void The_limiter_is_registered_as_a_singleton()
    {
        var services = Build();

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IEmailRateLimiter)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void The_calendar_invite_handler_is_registered()
    {
        Build().Should().Contain(d => d.ServiceType == typeof(IOutboxMessageHandler)
            && d.ImplementationType == typeof(CalendarEventInviteEmailOutboxHandler));
    }

    [Fact]
    public void Every_dependency_of_the_invite_handler_and_the_limiter_is_registered()
    {
        var services = Build();

        foreach (var type in new[] { typeof(CalendarEventInviteEmailOutboxHandler), typeof(TokenBucketEmailRateLimiter) })
        {
            foreach (var parameter in type.GetConstructors().Single().GetParameters())
            {
                var dependency = parameter.ParameterType;
                if (dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(ILogger<>))
                    continue;
                if (parameter.HasDefaultValue)
                    continue;

                // Options are registered open (IOptions<>, IOptionsSnapshot<>), so a closed dependency like
                // IOptions<EmailRateOptions> is satisfied by the open registration.
                services.Should().Contain(d => Satisfies(d.ServiceType, dependency), $"{type.Name} needs {dependency.Name}");
            }
        }
    }

    private static bool Satisfies(Type registered, Type dependency)
    {
        if (registered == dependency)
            return true;
        if (!registered.IsGenericType || !dependency.IsGenericType)
            return false;

        var open = registered.GetGenericTypeDefinition();
        var wanted = dependency.GetGenericTypeDefinition();

        if (open != wanted)
            return false;

        var registeredArguments = registered.GetGenericArguments();
        var wantedArguments = dependency.GetGenericArguments();
        for (var i = 0; i < wantedArguments.Length; i++)
        {
            // An open registration (IOptions<> with its own type parameter) serves any closed one.
            if (registeredArguments[i].IsGenericParameter)
                continue;
            if (!registeredArguments[i].IsAssignableFrom(wantedArguments[i]))
                return false;
        }

        return true;
    }

    [Fact]
    public void The_configured_max_per_second_reaches_the_limiter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=di_registration_test_only",
                ["Email:MaxPerSecond"] = "7"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<EmailRateOptions>>().Value.MaxPerSecond.Should().Be(7);
    }

    [Fact]
    public void The_shipped_appsettings_leave_the_rate_unlimited()
    {
        // 0 means unlimited. The limit is a deployment setting, so the shipped default must not pace
        // anything on its own.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ONEVO.Api", "appsettings.json"))
            .Build();

        var options = new EmailRateOptions();
        configuration.GetSection(EmailRateOptions.SectionName).Bind(options);

        options.MaxPerSecond.Should().Be(0);
    }

    [Fact]
    public void The_container_can_build_the_calendar_invite_handler()
    {
        // The outbox processor resolves every handler in one call, so the handler has to be constructible
        // with the limiter actually wired in - not merely registered.
        using var provider = Build().BuildServiceProvider();

        var handler = provider.GetServices<IOutboxMessageHandler>()
            .OfType<CalendarEventInviteEmailOutboxHandler>()
            .Should().ContainSingle().Subject;

        handler.Should().NotBeNull();
    }

    [Fact]
    public void The_container_shares_one_limiter_between_scopes()
    {
        // The pacing window has to survive a scope change: the outbox processor creates a scope per batch.
        using var provider = Build().BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IEmailRateLimiter>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IEmailRateLimiter>());
    }

    [Fact]
    public void ANegative_limit_stops_the_app_at_startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=di_registration_test_only",
                ["Encryption:MasterKey"] = "email_rate_limiter_registration_test_key",
                ["Email:MaxPerSecond"] = "-1"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmailRateOptions>>().Value);

        Assert.Contains("Email:MaxPerSecond", error.Message);
    }
}
