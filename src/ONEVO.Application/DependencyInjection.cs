using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.Monitoring.WorkSessions.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;
using ONEVO.Application.Features.OrgStructure.OutboxHandlers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;


namespace ONEVO.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        // Outbox message consumers (dispatched by the Infrastructure outbox worker).
        services.AddScoped<IOutboxMessageHandler, TenantOwnerInviteEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, PasswordResetEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, AdminPasswordResetEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, AdminPasswordChangedEmailOutboxHandler>();

        // Position lifecycle events have no real consumer yet - registered as no-ops so
        // OutboxProcessor doesn't retry-and-fail every position create/update/archive/restore.
        services.AddScoped<IOutboxMessageHandler>(_ => new NoOpPositionOutboxHandler(OutboxMessageTypes.PositionCreated));
        services.AddScoped<IOutboxMessageHandler>(_ => new NoOpPositionOutboxHandler(OutboxMessageTypes.PositionUpdated));
        services.AddScoped<IOutboxMessageHandler>(_ => new NoOpPositionOutboxHandler(OutboxMessageTypes.PositionArchived));
        services.AddScoped<IOutboxMessageHandler>(_ => new NoOpPositionOutboxHandler(OutboxMessageTypes.PositionRestored));

        services.AddScoped<IOutboxMessageHandler, PlatformManagerInviteEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, EmployeeOnboardingInviteEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, MonitoringWorkSessionCompletedOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, InvoiceEmailOutboxHandler>();
        services.AddScoped<GitHubUserIntegrationAvailability>();

        return services;
    }
}
