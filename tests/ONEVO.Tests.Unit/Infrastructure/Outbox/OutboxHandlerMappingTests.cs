using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Outbox;

/// <summary>
/// Task 31: every outbox message type has exactly one handler in the real registrations, and every handler handles a
/// known type. A type without a handler parks its messages as failed; two handlers for one type stop start-up.
/// </summary>
public sealed class OutboxHandlerMappingTests
{
    private static IServiceProvider BuildProvider(Action<IServiceCollection>? change = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=di_registration_test_only",
                ["Encryption:MasterKey"] = "outbox-mapping-test-encryption-master-key!",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        change?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<IOutboxMessageHandler> ResolveHandlers(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetServices<IOutboxMessageHandler>().ToList();
    }

    internal static IReadOnlyList<string> AllMessageTypes() => typeof(OutboxMessageTypes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    [Fact]
    public void EveryMessageType_HasExactlyOneHandler()
    {
        var handled = ResolveHandlers(BuildProvider()).GroupBy(h => h.Type).ToDictionary(g => g.Key, g => g.Count());

        foreach (var type in AllMessageTypes())
            handled.GetValueOrDefault(type).Should().Be(1, $"outbox type '{type}' needs exactly one handler");
    }

    [Fact]
    public void EveryHandler_HandlesAKnownMessageType()
    {
        var known = AllMessageTypes().ToHashSet(StringComparer.Ordinal);

        ResolveHandlers(BuildProvider()).Should().OnlyContain(h => known.Contains(h.Type),
            "a handler's Type must be one of the OutboxMessageTypes constants");
    }

    [Fact]
    public void MessageTypeValues_AreUnique()
    {
        AllMessageTypes().Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task StartupCheck_Passes_WithTheRealRegistrations()
    {
        var provider = BuildProvider();

        await new OutboxHandlerStartupCheck(provider.GetRequiredService<IServiceScopeFactory>()).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartupCheck_Fails_WhenATypeIsRegisteredTwice_NamingBothHandlers()
    {
        var provider = BuildProvider(s => s.AddScoped<IOutboxMessageHandler>(_ => new DuplicateHandler(OutboxMessageTypes.PasswordResetEmail)));

        var start = () => new OutboxHandlerStartupCheck(provider.GetRequiredService<IServiceScopeFactory>()).StartAsync(CancellationToken.None);

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(OutboxMessageTypes.PasswordResetEmail)
            .And.Contain("PasswordResetEmailOutboxHandler")
            .And.Contain(nameof(DuplicateHandler));
    }

    [Fact]
    public void Mapping_Fails_WhenAHandlerRegistrationIsRemoved()
    {
        var provider = BuildProvider(s =>
        {
            var registration = s.Single(d => d.ServiceType == typeof(IOutboxMessageHandler)
                && d.ImplementationType == typeof(ONEVO.Application.Features.Auth.Login.OutboxHandlers.PasswordResetEmailOutboxHandler));
            s.Remove(registration);
        });

        var handled = ResolveHandlers(provider).Select(h => h.Type).ToHashSet(StringComparer.Ordinal);

        AllMessageTypes().Where(t => !handled.Contains(t)).Should().Equal(OutboxMessageTypes.PasswordResetEmail);
    }

    [Fact]
    public void Map_RejectsAHandlerWithoutAType()
    {
        var build = () => OutboxHandlerMap.Build([new DuplicateHandler(" ")]);

        build.Should().Throw<InvalidOperationException>().WithMessage("*has no message type*");
    }

    private sealed class DuplicateHandler(string type) : IOutboxMessageHandler
    {
        public string Type { get; } = type;
        public Task HandleAsync(string payloadJson, CancellationToken ct) => Task.CompletedTask;
    }
}
