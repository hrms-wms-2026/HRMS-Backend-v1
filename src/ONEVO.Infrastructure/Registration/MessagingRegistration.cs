using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Security;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Transactional outbox, notifications, platform options, email sending and pacing, encryption.</summary>
internal static class MessagingRegistration
{
    internal static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Transactional outbox + idempotency
        services.AddScoped<IOutboxWriter, Services.SharedPlatform.Outbox.OutboxWriter>();

        // Per-scope "which outbox message is being dispatched" - feeds provider idempotency keys.
        services.AddScoped<Services.SharedPlatform.Outbox.OutboxDispatchContext>();
        services.AddScoped<IOutboxDispatchContext>(sp => sp.GetRequiredService<Services.SharedPlatform.Outbox.OutboxDispatchContext>());
        services.AddScoped<INotificationDispatcher, Services.SharedPlatform.Notifications.NotificationDispatcher>();
        services.AddSingleton<ONEVO.Application.Features.SharedPlatform.Notifications.Rendering.INotificationTemplateRenderer, Services.SharedPlatform.Notifications.NotificationTemplateRenderer>();
        services.AddScoped<ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces.INotificationTemplateStore, Persistence.Repositories.SharedPlatform.EfNotificationTemplateStore>();
        services.AddScoped<ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces.ITenantNotificationRuleRepository, Persistence.Repositories.SharedPlatform.EfTenantNotificationRuleRepository>();
        services.AddScoped<ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces.INotificationDeliveryRepository, Persistence.Repositories.SharedPlatform.EfNotificationDeliveryRepository>();
        services.AddScoped<ONEVO.Application.Features.SharedPlatform.Notifications.Delivery.INotificationOrchestrator, Services.SharedPlatform.Notifications.NotificationOrchestrator>();
        services.AddScoped<ONEVO.Application.Features.SharedPlatform.Notifications.Delivery.INotificationChannelDeliveryHandler, Services.SharedPlatform.Notifications.NotificationInAppDeliveryHandler>();
        services.AddScoped<ONEVO.Application.Features.SharedPlatform.Notifications.Delivery.INotificationChannelDeliveryHandler, Services.SharedPlatform.Notifications.NotificationEmailDeliveryHandler>();
        services.AddScoped<ONEVO.Application.Common.ServiceInterfaces.IOutboxMessageHandler, ONEVO.Application.Features.SharedPlatform.Notifications.Delivery.NotificationDeliveryOutboxHandler>();
        services.AddScoped<ONEVO.Application.Common.ServiceInterfaces.IOutboxMessageHandler, ONEVO.Application.Features.SharedPlatform.Notifications.Tray.NotificationTrayPushOutboxHandler>();
        services.AddScoped<IIdempotencyStore, Persistence.Repositories.SharedPlatform.Idempotency.EfIdempotencyStore>();

        // -- Platform options binding ------------------------------------------
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Services.DevPlatform.Support.SupportAlertOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Services.DevPlatform.Support.SupportAlertOptions.SectionName));
        services.Configure<PlatformUrlsOptions>(configuration.GetSection(PlatformUrlsOptions.SectionName));
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Configuration.StorageQuotaOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Configuration.StorageQuotaOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Configuration.FileStorageOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Configuration.FileStorageOptions.SectionName));
        services.Configure<ONEVO.Application.Features.Monitoring.ActivityMonitoring.Options.MonitoringRetentionOptions>(
            configuration.GetSection(ONEVO.Application.Features.Monitoring.ActivityMonitoring.Options.MonitoringRetentionOptions.SectionName));
        services.AddOptions<AwsRekognitionOptions>()
            .Bind(configuration.GetSection(AwsRekognitionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ONEVO.Application.Common.Configuration.BiometricEnrollmentOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Common.Configuration.BiometricEnrollmentOptions.SectionName));

        // -- Email: transactional sending via ONEVO-owned platform service keys --
        // Both the active transactional email provider and its API key are resolved
        // per send by joining platform_providers to platform_service_keys through
        // IPlatformServiceKeyResolver - never from appsettings or a hardcoded default.
        services.AddHttpClient(SendGridEmailAdapter.HttpClientName);
        services.AddHttpClient(ResendEmailAdapter.HttpClientName);
        services.AddScoped<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailProviderAdapter, SendGridEmailAdapter>();
        services.AddScoped<IEmailProviderAdapter, ResendEmailAdapter>();
        services.AddScoped<ITransactionalEmailSender, PlatformKeyTransactionalEmailSender>();
        services.AddScoped<IEmailService, TransactionalEmailService>();

        // -- Email: outbound pacing (Plan D Task 3) ------------------------------
        // Email:MaxPerSecond caps how many calendar invite emails may start per rolling second, per
        // instance. 0 (the default) means unlimited. The limiter is a singleton: the window is shared by
        // every send in this process, so a scoped limiter would reset on every scope.
        services.Configure<EmailRateOptions>(configuration.GetSection(EmailRateOptions.SectionName));
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<EmailRateOptions>, EmailRateOptionsValidator>();
        services.AddOptions<EmailRateOptions>().ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEmailRateLimiter, TokenBucketEmailRateLimiter>();

        services.AddScoped<IEncryptionService, AesEncryptionService>();

        return services;
    }
}
