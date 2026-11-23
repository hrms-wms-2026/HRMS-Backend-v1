using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Api.Hubs;
using ONEVO.Api.Middleware;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Api.Extensions;

/// <summary>
/// Task 25: SignalR, the hub-backed notifiers and the presence trackers, moved out of Program.cs unchanged and in
/// the same order. The returned builder is where the Redis backplane is added (task 21, SharedStateExtensions).
/// </summary>
public static class RealtimeExtensions
{
    public static ISignalRServerBuilder AddApiRealtime(this IServiceCollection services)
    {
        var signalR = services.AddSignalR();
        services.AddSingleton<ITrayPolicyRefreshNotifier, SignalRTrayPolicyRefreshNotifier>();
        // Inbox realtime: replaces Infrastructure's null publisher with the InboxHub-backed one.
        services.Replace(ServiceDescriptor.Scoped<IInboxRealtimePublisher, SignalRInboxRealtimePublisher>());
        services.AddSingleton<ITrayPushNotifier, SignalRTrayPushNotifier>();
        services.AddSingleton<TrayConnectionTracker>();
        services.AddSingleton<ITrayConnectionPresence>(sp => sp.GetRequiredService<TrayConnectionTracker>());
        services.AddSingleton<WebActivityTracker>();
        services.AddSingleton<IWebActivityPresence>(sp => sp.GetRequiredService<WebActivityTracker>());
        return signalR;
    }
}
