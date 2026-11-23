using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Services;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.ExternalServices.GitHub;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.Calendar;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Calendar events, external calendar and meeting providers, OAuth clients, tenant integrations and calendar exceptions.</summary>
internal static class CalendarIntegrationsRegistration
{
    internal static IServiceCollection AddCalendarAndIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfCalendarEventRepository>();
        services.AddScoped<ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventRepository>(
            sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfCalendarEventRepository>());
        services.AddScoped<IExternalCalendarConnectionRepository, EfExternalCalendarConnectionRepository>();
        services.AddScoped<IExternalCalendarEventLinkRepository, EfExternalCalendarEventLinkRepository>();
        services.AddScoped<ICalendarEventMeetingRepository, EfCalendarEventMeetingRepository>();
        services.AddScoped<ICalendarEventMeetingAttendanceRepository, EfCalendarEventMeetingAttendanceRepository>();
        services.AddScoped<ICalendarRecurrenceExpander, IcalNetRecurrenceExpander>();
        services.AddScoped<ICalendarNotificationSender, CalendarNotificationSender>();
        services.AddScoped<IMeetingProvisioner, MeetingProvisioner>();
        services.AddScoped<EventMeetingLinkService>();
        services.AddScoped<ICalendarTimezoneResolver, CalendarTimezoneResolver>();

        // Tenant-scoped connected integration credentials
        services.AddScoped<ITenantIntegrationCredentialRepository, EfTenantIntegrationCredentialRepository>();
        services.AddScoped<ITenantIntegrationCredentialResolver, TenantIntegrationCredentialResolver>();
        services.AddScoped<IUserIntegrationConnectionRepository, EfUserIntegrationConnectionRepository>();
        services.AddDataProtection();
        services.AddSingleton<IOAuthStateProtector, OAuthStateProtector>();
        services.AddScoped<ICalendarOAuthStateProtector, CalendarOAuthStateProtector>();
        services.AddHttpClient<IGitHubOAuthClient, GitHubOAuthTokenClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<ICalendarOAuthTokenExchangeClient, ONEVO.Infrastructure.ExternalServices.Calendar.CalendarOAuthTokenExchangeClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
        services.AddHttpClient<IMicrosoftGraphCalendarClient, MicrosoftGraphCalendarClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
        services.AddHttpClient<ITeamsMeetingClient, MicrosoftGraphMeetingClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
        services.AddHttpClient<IZoomMeetingClient, ZoomMeetingClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
        // Per-occurrence attendance sync (meetings Phase 2, spec 5.8): one session client per provider, over the same typed clients.
        services.AddTransient<ONEVO.Application.Features.Calendar.ServiceInterfaces.IMeetingAttendanceSessionClient>(
            sp => (MicrosoftGraphMeetingClient)sp.GetRequiredService<ITeamsMeetingClient>());
        services.AddTransient<ONEVO.Application.Features.Calendar.ServiceInterfaces.IMeetingAttendanceSessionClient>(
            sp => (ZoomMeetingClient)sp.GetRequiredService<IZoomMeetingClient>());
        services.AddScoped<ICalendarConnectionTokenProvider, CalendarConnectionTokenProvider>();
        services.AddScoped<ICalendarSyncService, CalendarSyncService>();
        services.AddScoped<ONEVO.Application.Features.Calendar.Services.CalendarEventReminderService>();

        // Calendar meeting exceptions (plan C2).
        services.AddScoped<ONEVO.Application.Features.Calendar.Exceptions.ICalendarIncidentRepository, ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfCalendarIncidentRepository>();
        foreach (var rule in new[] { typeof(ONEVO.Application.Features.Calendar.Exceptions.NoShowRule), typeof(ONEVO.Application.Features.Calendar.Exceptions.LateJoinRule),
                     typeof(ONEVO.Application.Features.Calendar.Exceptions.EarlyLeaveRule), typeof(ONEVO.Application.Features.Calendar.Exceptions.NoResponseRule),
                     typeof(ONEVO.Application.Features.Calendar.Exceptions.DeclineRateRule), typeof(ONEVO.Application.Features.Calendar.Exceptions.LateDeclineRule),
                     typeof(ONEVO.Application.Features.Calendar.Exceptions.UnresolvedRequestRule), typeof(ONEVO.Application.Features.Calendar.Exceptions.DoubleBookRule),
                     typeof(ONEVO.Application.Features.Calendar.Exceptions.OnLeaveRule), typeof(ONEVO.Application.Features.Calendar.Exceptions.OverloadRule),
                     typeof(ONEVO.Application.Features.Calendar.Exceptions.OffHoursRule) })
            services.AddSingleton(typeof(ONEVO.Application.Features.Calendar.Exceptions.ICalendarIncidentRule), rule);
        services.AddScoped<ONEVO.Application.Features.Calendar.Exceptions.CalendarCaseService>();
        services.AddScoped<ONEVO.Application.Features.Calendar.Exceptions.CalendarIncidentEngine>();
        // Task meetings feed the same C2 rules as calendar events (meetings Phase 2, spec 10).
        services.AddScoped<ONEVO.Application.Features.Calendar.Exceptions.IEmployeeMeetingOccurrenceSource,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.TaskMeetingOccurrenceSource>();
        services.AddScoped<ONEVO.Application.Common.ServiceInterfaces.IOutboxMessageHandler, ONEVO.Application.Features.Calendar.OutboxHandlers.CalendarIncidentEvaluateOutboxHandler>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationSweepSource, ONEVO.Application.Features.Calendar.Exceptions.CalendarExceptionEscalationSweepSource>();

        services.AddScoped<ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource, ONEVO.Application.Features.Dashboard.Team.Sources.CalendarMeetingExceptionTeamActionSource>();

        return services;
    }
}
