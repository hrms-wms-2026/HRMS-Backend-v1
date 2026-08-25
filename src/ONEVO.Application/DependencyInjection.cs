using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.Onboarding.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Billing.OutboxHandlers;
using ONEVO.Application.Features.OrgStructure.OutboxHandlers;
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingTemplate;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;


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

        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.Services.IClockInPolicyScopeMembershipValidator,
            ONEVO.Application.Features.TimeAttendance.Services.ClockInPolicyScopeMembershipValidator>();
        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.Services.IAttendanceTodayStateService,
            ONEVO.Application.Features.TimeAttendance.Services.AttendanceTodayStateService>();

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
        services.AddScoped<IOutboxMessageHandler>(_ => new NoOpEmployeeSecurityOutboxHandler(OutboxMessageTypes.EmployeeSecurityUpdated));

        services.AddScoped<IOutboxMessageHandler, PlatformManagerInviteEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, EmployeeOnboardingInviteEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, PositionChangeApprovalRequestEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, InvoiceEmailOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler>(_ =>
            new NoOpLeaveApprovalSideEffectOutboxHandler(OutboxMessageTypes.LeaveRequestApproved));
        services.AddScoped<IOutboxMessageHandler>(_ =>
            new NoOpLeaveApprovalSideEffectOutboxHandler(OutboxMessageTypes.LeaveRequestRejected));
        services.AddScoped<IOutboxMessageHandler>(_ =>
            new NoOpLeaveApprovalSideEffectOutboxHandler(OutboxMessageTypes.LeaveInformationRequested));
        services.AddScoped<IOutboxMessageHandler, ONEVO.Application.Features.Leave.Cancellation.Outbox.NoOpLeaveCancellationSideEffectOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, WorkNotificationOutboxHandler>();
        services.AddScoped<GitHubUserIntegrationAvailability>();
        services.AddScoped<GetBulkOnboardingTemplateQueryHandler>();

        services.AddScoped<
            ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces.IEmployeeAuthorityResolver,
            ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.EmployeeAuthorityResolver>();

        return services;
    }
}
