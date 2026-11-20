using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Services.Calendar;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Leave types, policies, entitlements, requests, approvals, calendar and cancellation.</summary>
internal static class LeaveRegistration
{
    internal static IServiceCollection AddLeave(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<
            ONEVO.Application.Features.Leave.Type.RepositoryInterfaces.ILeaveTypeRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Type.EfLeaveTypeRepository>();

        services.AddScoped<
            ONEVO.Application.Features.Leave.Request.RepositoryInterfaces.ILeaveRequestReadRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Request.EfLeaveRequestReadRepository>();

        services.AddScoped<
            ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces.ILeavePolicyRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy.EfLeavePolicyRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces.ILeavePolicyResolutionRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy.EfLeavePolicyResolutionRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Policy.Services.ILeavePolicyResolver,
            ONEVO.Application.Features.Leave.Policy.Services.LeavePolicyResolver>();
        services.AddScoped<ONEVO.Application.Features.Leave.Policy.Services.LeavePolicyAssignmentService>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces.ILeaveEntitlementRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Entitlement.EfLeaveEntitlementRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces.ILeaveBalanceAuditRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.BalanceAudit.EfLeaveBalanceAuditRepository>();
        services.AddScoped<ONEVO.Application.Features.Leave.Entitlement.Helpers.ILeaveWorkingDayCounter,
            ONEVO.Application.Features.Leave.Entitlement.Helpers.LeaveWorkingDayCounter>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Entitlement.Services.ILeaveCreditService,
            ONEVO.Application.Features.Leave.Entitlement.Services.LeaveCreditService>();

        services.AddOptions<ONEVO.Application.Features.Leave.Entitlement.Options.LeaveEntitlementYearOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Features.Leave.Entitlement.Options.LeaveEntitlementYearOptions.SectionName))
            .Validate(options => options.MinimumYear > 0, "Leave entitlement minimum year must be configured.")
            .Validate(options => options.MaximumYear >= options.MinimumYear, "Leave entitlement maximum year must be after the minimum year.")
            .ValidateOnStart();
        services.AddOptions<ONEVO.Application.Features.Leave.Request.Options.LeaveRequestOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Features.Leave.Request.Options.LeaveRequestOptions.SectionName))
            .Validate(options => options.MaximumRequestRangeDays > 0, "Leave request maximum range days must be configured.")
            .ValidateOnStart();
        services.AddOptions<ONEVO.Application.Features.Leave.Calendar.Options.LeaveCalendarOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Features.Leave.Calendar.Options.LeaveCalendarOptions.SectionName))
            .ValidateOnStart();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Request.RepositoryInterfaces.ILeaveRequestRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Request.EfLeaveRequestRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces.ILeaveCalendarRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar.EfLeaveCalendarRepository>();
        services.AddSingleton<ONEVO.Application.Features.Leave.Request.Helpers.LeaveRequestHourCalculator>();
        services.AddSingleton<ONEVO.Application.Features.Leave.Calendar.Helpers.LeaveCalendarRequestProjector>();
        services.AddHttpClient<ONEVO.Application.Features.Calendar.ServiceInterfaces.INagerHolidaysClient,
            ONEVO.Infrastructure.Services.Calendar.NagerHolidaysClient>(client =>
        {
            client.BaseAddress = new Uri("https://date.nager.at/api/v3/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<ONEVO.Application.Features.Calendar.RepositoryInterfaces.IHolidayCalendarSettingsRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfHolidayCalendarSettingsRepository>();
        services.AddScoped<ONEVO.Application.Features.Leave.Calendar.Services.ILeaveCalendarHolidayProvider,
            ONEVO.Infrastructure.Services.Calendar.NagerHolidaysProvider>();
        services.AddScoped<ONEVO.Application.Features.Leave.Calendar.Services.ILeaveVisibilityScopeProvider,
            ONEVO.Application.Features.Leave.Calendar.Services.LeaveVisibilityScopeProvider>();
        services.AddScoped<ONEVO.Application.Features.Leave.Request.Services.ILeaveRequestConflictProvider,
            ONEVO.Infrastructure.Services.Leave.EfLeaveRequestConflictProvider>();
        services.AddScoped<ONEVO.Application.Features.Leave.Request.Services.ILeaveApproverResolver,
            ONEVO.Application.Features.Leave.Request.Services.LeaveApproverResolver>();
        services.AddScoped<ONEVO.Application.Features.Leave.Request.Services.ILeaveTeamAbsenceWarningService,
            ONEVO.Application.Features.Leave.Request.Services.LeaveTeamAbsenceWarningService>();
        services.AddScoped<ONEVO.Application.Features.Leave.Request.Services.LeaveRequestSubmissionEvaluator>();
        services.AddOptions<ONEVO.Application.Features.Leave.Approval.Options.LeaveApprovalOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Features.Leave.Approval.Options.LeaveApprovalOptions.SectionName))
            .ValidateOnStart();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces.ILeaveApprovalRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Approval.EfLeaveApprovalRepository>();
        services.AddScoped<ONEVO.Application.Features.Leave.Approval.Commands.LeaveApprovalDecisionService>();
        services.AddScoped<ONEVO.Application.Features.Leave.Approval.Services.LeaveRequestRangeEditor>();

        services.AddOptions<ONEVO.Application.Features.Leave.Cancellation.Options.LeaveCancellationOptions>()
            .Bind(configuration.GetSection(ONEVO.Application.Features.Leave.Cancellation.Options.LeaveCancellationOptions.SectionName))
            .Validate(
                options => ONEVO.Application.Features.Leave.Cancellation.Options.LeaveCancellationOptions.IsValidTimezone(options.FallbackTimezone),
                "Leave:Cancellation:FallbackTimezone must be a valid timezone id.")
            .ValidateOnStart();
        services.AddSingleton<ONEVO.Application.Features.Leave.Cancellation.Helpers.LeaveCancellationClassifier>();
        services.AddSingleton<ONEVO.Application.Features.Leave.Cancellation.Helpers.LeaveRequestDayAllocationBuilder>();
        services.AddScoped<ONEVO.Application.Features.Leave.Cancellation.Helpers.LeaveBusinessDateResolver>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Cancellation.RepositoryInterfaces.ILeaveCancellationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Cancellation.EfLeaveCancellationRepository>();

        return services;
    }
}
