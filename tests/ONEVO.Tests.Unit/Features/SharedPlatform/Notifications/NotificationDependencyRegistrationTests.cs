using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ONEVO.Application;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

/// <summary>
/// The outbox processor resolves every IOutboxMessageHandler in one call, so a single missing dependency
/// would stop all outbox messages (password resets included). Check the new notification types against the
/// real registrations instead of the hand-built objects the other tests use.
/// </summary>
public class NotificationDependencyRegistrationTests
{
    private static readonly IServiceCollection Services = Build();

    private static IServiceCollection Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=di_registration_test_only"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services;
    }

    [Theory]
    [InlineData(typeof(INotificationOrchestrator), typeof(NotificationOrchestrator))]
    [InlineData(typeof(IWorkNotificationEngine), typeof(WorkNotificationEngine))]
    public void NewServices_AreRegistered(Type service, Type implementation)
    {
        Services.Should().Contain(d => d.ServiceType == service && d.ImplementationType == implementation);
    }

    [Fact]
    public void DeliveryOutboxHandler_AndBothChannelHandlers_AreRegistered()
    {
        Services.Should().Contain(d => d.ServiceType == typeof(IOutboxMessageHandler) && d.ImplementationType == typeof(NotificationDeliveryOutboxHandler));
        Services.Where(d => d.ServiceType == typeof(INotificationChannelDeliveryHandler)).Select(d => d.ImplementationType)
            .Should().BeEquivalentTo([typeof(NotificationInAppDeliveryHandler), typeof(NotificationEmailDeliveryHandler)]);
    }

    [Theory]
    [InlineData(typeof(NotificationDeliveryOutboxHandler))]
    [InlineData(typeof(NotificationInAppDeliveryHandler))]
    [InlineData(typeof(NotificationEmailDeliveryHandler))]
    [InlineData(typeof(NotificationOrchestrator))]
    [InlineData(typeof(WorkNotificationEngine))]
    [InlineData(typeof(SendTestNotificationCommandHandler))]
    [InlineData(typeof(UpdateNotificationSettingCommandHandler))]
    [InlineData(typeof(ONEVO.Application.Features.SharedPlatform.Notifications.Tray.NotificationTrayPushOutboxHandler))]
    [InlineData(typeof(ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Commands.AckTrayDelivery.AckTrayDeliveryCommandHandler))]
    [InlineData(typeof(ONEVO.Application.Features.SharedPlatform.Notifications.Tray.Queries.GetPendingTrayDeliveries.GetPendingTrayDeliveriesQueryHandler))]
    public void EveryConstructorDependency_IsRegistered(Type type)
    {
        var parameters = type.GetConstructors().Single().GetParameters();

        foreach (var parameter in parameters)
        {
            var dependency = parameter.ParameterType;
            if (dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(ILogger<>))
                continue;
            if (parameter.HasDefaultValue)
                continue; // optional (e.g. ITrayPushNotifier is registered by the API host only)
            if (dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                dependency = dependency.GetGenericArguments()[0];

            Services.Should().Contain(d => d.ServiceType == dependency, $"{type.Name} needs {dependency.Name}");
        }
    }
}
