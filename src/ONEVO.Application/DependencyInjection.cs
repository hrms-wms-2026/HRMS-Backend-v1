using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers;
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
        services.AddScoped<GitHubUserIntegrationAvailability>();

        return services;
    }
}
