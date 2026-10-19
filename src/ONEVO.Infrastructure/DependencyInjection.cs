using ONEVO.Application.Features.Inbox.Contracts;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Services.Calendar;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Services;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Compliance;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.ExternalIdentity;
using ONEVO.Infrastructure.Identity.Mfa;
using ONEVO.Infrastructure.Identity.Passwords;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.ExternalServices.GitHub;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Subscription;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.DevPlatform.PlatformAccess;
using ONEVO.Infrastructure.Services.DevPlatform.Provisioning;
using ONEVO.Infrastructure.Services.DevPlatform.Tenancy;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.TrayActivation;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.WorkSessions;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;
using ONEVO.Infrastructure.Services.Auth.Login;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;
using ONEVO.Infrastructure.Services.Monitoring.CheckIn;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Infrastructure.Services.SystemConfig;

namespace ONEVO.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();
        services.AddScoped<TenantRlsInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(3);
                    npgsql.CommandTimeout(30);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IRoleTemplateRepository, EfRoleTemplateRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
        services.AddScoped<ISessionRepository, EfSessionRepository>();
        services.AddScoped<ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces.IUserAccountRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Auth.UserAccounts.EfUserAccountRepository>();
        services.AddScoped<IPasswordResetTokenRepository, EfPasswordResetTokenRepository>();
        services.AddScoped<IUserMfaRepository, EfUserMfaRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
        services.AddScoped<IPermissionRepository, EfPermissionRepository>();
        services.AddScoped<IUserPermissionOverrideRepository, EfUserPermissionOverrideRepository>();
        services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
        services.AddScoped<IFeatureAccessGrantRepository, EfFeatureAccessGrantRepository>();
        services.AddScoped<IAuditLogRepository, EfAuditLogRepository>();

        // Developer Platform repositories
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantStatusHistoryRepository, EfTenantStatusHistoryRepository>();
        services.AddScoped<EfLegalEntityRepository>();
        services.AddScoped<ILegalEntityRepository>(sp => sp.GetRequiredService<EfLegalEntityRepository>());
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
        services.AddScoped<IClockInPolicyRepository, EfClockInPolicyRepository>();
        services.AddScoped<IAttendanceReadRepository, EfAttendanceReadRepository>();
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertRepository>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertRepository>(sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertRepository>());
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertResolutionLookup>(sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertRepository>());
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertMaterializer, ONEVO.Application.Features.TimeAttendance.Alerts.AttendanceAlertMaterializer>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Type.RepositoryInterfaces.ILeaveTypeRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Type.EfLeaveTypeRepository>();

                services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces.IAttendanceCorrectionRepository,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceCorrectionRepository>();
        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces.IWorkAreaChangeRequestRepository,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfWorkAreaChangeRequestRepository>();
        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces.IEmployeeWorkLocationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfEmployeeWorkLocationRepository>();
        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces.ILocationChangeRequestRepository,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfLocationChangeRequestRepository>();
        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces.IDailyWorkLocationConfirmationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfDailyWorkLocationConfirmationRepository>();

        services.AddScoped<
            ONEVO.Application.Features.Leave.Request.RepositoryInterfaces.ILeaveRequestReadRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Request.EfLeaveRequestReadRepository>();

        services.AddScoped<
            ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces.ILeavePolicyRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy.EfLeavePolicyRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces.ILeaveEntitlementRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.Entitlement.EfLeaveEntitlementRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces.ILeaveBalanceAuditRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Leave.BalanceAudit.EfLeaveBalanceAuditRepository>();
        services.AddScoped<ONEVO.Application.Features.Leave.Entitlement.Helpers.ILeaveWorkingDayCounter,
            ONEVO.Application.Features.Leave.Entitlement.Helpers.LeaveWorkingDayCounter>();
        services.AddScoped<ONEVO.Application.Features.Leave.Entitlement.Helpers.LeaveEntitlementCalculator>();
        services.AddScoped<ONEVO.Application.Features.Leave.Entitlement.Helpers.LeaveEntitlementPlanner>();
        services.AddHostedService<ONEVO.Infrastructure.Services.Leave.LeaveYearEndEntitlementJob>();
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
            .Validate(options =>
                ONEVO.Application.Features.Leave.Calendar.Options.LeaveCalendarOptions.AreColorsValid(options.TypeCategoryColors),
                "Leave:Calendar:TypeCategoryColors must contain #RRGGBB hex colors.")
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
        // Monitoring exception cases in the Inbox (plan X1).
        services.AddInboxKind(ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionInboxKinds.Descriptor);
        services.AddScoped<ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseActions>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionConversationSubjectResolver>();
        // A reviewer's reply on an open monitoring or calendar case acknowledges it.
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationReplyHook, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseReplyAcknowledger>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseReverter>();
        // Calendar meeting-exception cases in the Inbox (plan C3).
        services.AddInboxKind(ONEVO.Application.Features.Calendar.Inbox.CalendarInboxKinds.Descriptor);
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.Calendar.Inbox.CalendarInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.Calendar.Inbox.CalendarConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.Calendar.Inbox.CalendarMeetingExceptionReverter>();
        // Attendance requests in the Inbox (plan A1).
        foreach (var kind in ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceInboxKinds.Descriptors)
            services.AddInboxKind(kind);
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceAlertInbox>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceAlertReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider>(sp => sp.GetRequiredService<ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceInboxSourceProvider>());
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceCorrectionReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.WorkModeChangeReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.LocationChangeReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.DeviceChangeReverter>();
        // Leave in the Inbox (plan L1).
        services.AddInboxKind(ONEVO.Application.Features.Leave.Inbox.LeaveInboxKinds.Descriptor);
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.Leave.Inbox.LeaveInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.Leave.Inbox.LeaveConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.Leave.Inbox.LeaveDecisionReverter>();
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

        services.AddScoped<IExpectedWorkAreaResolver, ExpectedWorkAreaResolver>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces.IMonitoringPolicyConfigurationService,
            ONEVO.Infrastructure.Services.Monitoring.Settings.MonitoringPolicyConfigurationService>();

        services.AddScoped<IPositionAssignmentRepository, EfPositionAssignmentRepository>();
        services.AddScoped<IEmployeeHierarchyClosureRepository, EfEmployeeHierarchyClosureRepository>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository,
            ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeProfileRepository,
            ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeProfileRepository>();
        services.AddScoped<IEmployeeVisibilityScopeResolver, EmployeeVisibilityScopeResolver>();
        services.AddScoped<ISeatEntitlementService, SeatEntitlementService>();
        services.AddScoped<IOnboardingDraftRepository, EfOnboardingDraftRepository>();
        services.AddScoped<IOnboardingDraftWriteService, OnboardingDraftWriteService>();
        services.AddScoped<IBulkOnboardingBatchRepository, EfBulkOnboardingBatchRepository>();
        services.AddScoped<IBulkOnboardingRowValidator, BulkOnboardingRowValidator>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch.IBulkOnboardingValidationRunner,
            ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch.BulkOnboardingValidationRunner>();
        services.AddScoped<IAccessGrantRequestRepository, EfAccessGrantRequestRepository>();
        services.AddScoped<ISeatIncreaseRequestRepository, EfSeatIncreaseRequestRepository>();
        services.AddScoped<IOnboardingInboxRepository, EfOnboardingInboxRepository>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Onboarding.Services.OnboardingApprovers>();
        // Position access and seat increase approvals in the Inbox.
        foreach (var kind in ONEVO.Application.Features.CoreHr.Onboarding.Inbox.OnboardingInboxKinds.Descriptors)
            services.AddInboxKind(kind);
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.CoreHr.Onboarding.Inbox.OnboardingInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.CoreHr.Onboarding.Inbox.OnboardingConversationSubjectResolver>();
        services.AddScoped<IChecklistTemplateRepository, EfChecklistTemplateRepository>();
        services.AddScoped<IEmployeeChecklistTaskRepository, EfEmployeeChecklistTaskRepository>();
        services.AddScoped<IOfficeProjectProvisioner, ONEVO.Infrastructure.Services.CoreHr.OfficeProjectProvisioner>();
        services.AddScoped<IOfficeProjectReconciler, ONEVO.Infrastructure.Services.CoreHr.OfficeProjectReconciler>();
        services.AddHostedService<ONEVO.Infrastructure.Services.CoreHr.OfficeProjectReconciliationStartupService>();
        services.AddScoped<IEmployeeChecklistWorkTaskProvisioner, EmployeeChecklistWorkTaskProvisioner>();
        services.AddScoped<IOffboardingRecordRepository, EfOffboardingRecordRepository>();
        services.AddScoped<IOffboardingTaskBypassRequestRepository, EfOffboardingTaskBypassRequestRepository>();
        services.AddScoped<IEmployeeOffboardingLockGuard, ONEVO.Infrastructure.Services.CoreHr.Offboarding.EmployeeOffboardingLockGuard>();
        services.AddScoped<IEmployeeOffboardingCoverageGuard, ONEVO.Infrastructure.Services.CoreHr.Offboarding.EmployeeOffboardingCoverageGuard>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces.IEmployeeManageScopeGuard, ONEVO.Infrastructure.Services.CoreHr.EmployeeManageScopeGuard>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces.IEmployeeReadAccessGuard, ONEVO.Infrastructure.Services.CoreHr.EmployeeReadAccessGuard>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeActivityFeedRepository, ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeActivityFeedRepository>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Onboarding.ServiceInterfaces.IChecklistTemplateAssigneeResolver, ONEVO.Infrastructure.Services.CoreHr.Onboarding.ChecklistTemplateAssigneeResolver>();
        services.AddScoped<ONEVO.Application.Features.CoreHr.Onboarding.Services.ChecklistTemplateTaskInputResolver>();
        services.AddScoped<IWorkModeRepository, EfWorkModeRepository>();
        services.AddScoped<IWorkModeSeeder, ONEVO.Infrastructure.Services.TimeAttendance.WorkModeSeeder>();
        services.AddScoped<IEmploymentTypeRepository, EfEmploymentTypeRepository>();
        services.AddScoped<EfSubscriptionRepository>();
        services.AddScoped<ISubscriptionPlanRepository>(sp => sp.GetRequiredService<EfSubscriptionRepository>());
        services.AddScoped<ITenantSubscriptionRepository>(sp => sp.GetRequiredService<EfSubscriptionRepository>());
        services.AddScoped<EfSubscriptionInvoiceRepository>();
        services.AddScoped<ISubscriptionInvoiceRepository>(sp => sp.GetRequiredService<EfSubscriptionInvoiceRepository>());
        services.AddScoped<EfBillingAuditLogRepository>();
        services.AddScoped<IBillingAuditLogRepository>(sp => sp.GetRequiredService<EfBillingAuditLogRepository>());

        // Auth: invitation tokens
        services.AddScoped<IInvitationTokenRepository, EfInvitationTokenRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();
        services.AddScoped<IModuleCatalogRepository, ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ModuleCatalog.ModuleCatalogRepository>();

        // Work Management - Foundation slice
        services.AddScoped<EfProjectCategoryRepository>();
        services.AddScoped<IProjectCategoryRepository>(sp => sp.GetRequiredService<EfProjectCategoryRepository>());
        services.AddScoped<EfProjectRepository>();
        services.AddScoped<IProjectRepository>(sp => sp.GetRequiredService<EfProjectRepository>());
                services.AddScoped<EfObjectiveRepository>();
        services.AddScoped<IObjectiveRepository>(sp => sp.GetRequiredService<EfObjectiveRepository>());
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.EfCalendarEventRepository>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventRepository>(
            sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.EfCalendarEventRepository>());
        services.AddScoped<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventActivityLogRepository, EfCalendarEventActivityLogRepository>();

        services.AddScoped<EfTaskStatusRepository>();
        services.AddScoped<ITaskStatusRepository>(sp => sp.GetRequiredService<EfTaskStatusRepository>());
        services.AddScoped<EfTaskCategoryRepository>();
        services.AddScoped<ITaskCategoryRepository>(sp => sp.GetRequiredService<EfTaskCategoryRepository>());
        services.AddScoped<EfWorkTaskRepository>();
        services.AddScoped<IWorkTaskRepository>(sp => sp.GetRequiredService<EfWorkTaskRepository>());
        // Meetings-as-tasks Phase 2 (spec 8): every flag off by default; bad attendance settings or BulkInvite without
        // the outbox claim fix (P-1) stop the app at startup.
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions>,
            ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptionsValidator>();
        services.AddOptions<ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions>()
            .Configure(options => ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions.BindFrom(
                configuration.GetSection(ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions.Section), options))
            .ValidateOnStart();
        services.AddHostedService<Services.WorkManagement.Meetings.MeetingsStartupCheck>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings.EfMeetingRepository>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingAccessResolver,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingAccessResolver>();
        services.AddSingleton<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingRecurrence,
            Services.WorkManagement.Meetings.IcalNetMeetingRecurrence>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingCreateValidator>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingPlacementResolver,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacementResolver>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingReadService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingOccurrenceMaterializer,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingOccurrenceMaterializer>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingSeriesSplitter,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingSeriesSplitter>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingEditService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingCancelService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingAttendeeService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingMoveService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.SeriesMeetingLinkService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingOccurrenceGenerator>();
        services.AddHostedService<Services.WorkManagement.Meetings.MeetingOccurrenceGeneratorJob>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingAttendanceSyncService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingAttendanceReadService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingCalendarProjection>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingAttendanceRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings.EfMeetingAttendanceRepository>();
        services.AddHostedService<Services.WorkManagement.Meetings.MeetingAttendanceJob>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingSeriesFactory,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingSeriesFactory>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingTaskDefaults,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingTaskDefaults>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingOccurrenceWriter,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings.EfMeetingOccurrenceWriter>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingNotifier,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingNotifier>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingLinkRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings.EfMeetingLinkRepository>();
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
        services.AddScoped<EfSprintRepository>();
        services.AddScoped<ISprintRepository>(sp => sp.GetRequiredService<EfSprintRepository>());
        services.AddScoped<EfSprintActivityLogRepository>();
        services.AddScoped<ISprintActivityLogRepository>(sp => sp.GetRequiredService<EfSprintActivityLogRepository>());
        services.AddScoped<EfTaskAssignmentRepository>();
        services.AddScoped<ITaskAssignmentRepository>(sp => sp.GetRequiredService<EfTaskAssignmentRepository>());
        services.AddScoped<EfWorkApprovalRequestRepository>();
        services.AddScoped<IWorkApprovalRequestRepository>(sp => sp.GetRequiredService<EfWorkApprovalRequestRepository>());
        services.AddScoped<EfWorkNotificationLogRepository>();
        services.AddScoped<IWorkNotificationLogRepository>(sp => sp.GetRequiredService<EfWorkNotificationLogRepository>());
        services.AddScoped<IMonitorAlertRepository, EfMonitorAlertRepository>();
        services.AddScoped<EfTaskEditLogRepository>();
        services.AddScoped<ITaskEditLogRepository>(sp => sp.GetRequiredService<EfTaskEditLogRepository>());
        services.AddScoped<EfTaskStatusChangeLogRepository>();
        services.AddScoped<ITaskStatusChangeLogRepository>(sp => sp.GetRequiredService<EfTaskStatusChangeLogRepository>());
        services.AddScoped<ITaskStatusChangeAccessService, TaskStatusChangeAccessService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Leadership.Services.IWorkApprovalEligibility,
            ONEVO.Application.Features.WorkManagement.Leadership.Services.WorkApprovalEligibility>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Leadership.Services.IWorkLeadershipService,
            ONEVO.Application.Features.WorkManagement.Leadership.Services.WorkLeadershipService>();
        services.AddScoped<ITaskStatusChangeRequestConflictSweeper, TaskStatusChangeRequestConflictSweeper>();
        services.AddScoped<EfTaskClockingSessionRepository>();
        services.AddScoped<ITaskClockingSessionRepository>(sp => sp.GetRequiredService<EfTaskClockingSessionRepository>());
        services.AddScoped<EfTaskPercentageLogRepository>();
        services.AddScoped<ITaskPercentageLogRepository>(sp => sp.GetRequiredService<EfTaskPercentageLogRepository>());
        services.AddScoped<EfTaskCommentRepository>();
        services.AddScoped<ITaskCommentRepository>(sp => sp.GetRequiredService<EfTaskCommentRepository>());
        services.AddScoped<EfTaskDraftRepository>();
        services.AddScoped<ITaskDraftRepository>(sp => sp.GetRequiredService<EfTaskDraftRepository>());
        services.AddScoped<EfTaskCommentLogRepository>();
        services.AddScoped<ITaskCommentLogRepository>(sp => sp.GetRequiredService<EfTaskCommentLogRepository>());
        services.AddScoped<EfTaskCommentReactionRepository>();
        services.AddScoped<ITaskCommentReactionRepository>(sp => sp.GetRequiredService<EfTaskCommentReactionRepository>());

        services.AddScoped<EfNotificationRepository>();
        services.AddScoped<INotificationRepository>(sp => sp.GetRequiredService<EfNotificationRepository>());
        services.AddScoped<EfProjectMemberRepository>();
        services.AddScoped<IProjectMemberRepository>(sp => sp.GetRequiredService<EfProjectMemberRepository>());
        services.AddScoped<EfProjectMemberInvitationRepository>();
        services.AddScoped<IProjectMemberInvitationRepository>(sp => sp.GetRequiredService<EfProjectMemberInvitationRepository>());
        services.AddScoped<IWorkApprovalHistoryRepository, EfWorkApprovalHistoryRepository>();
        services.AddScoped<EfProjectVersionRepository>();
        services.AddScoped<IProjectVersionRepository>(sp => sp.GetRequiredService<EfProjectVersionRepository>());
        services.AddScoped<EfReleaseCalendarRepository>();
        services.AddScoped<IReleaseCalendarRepository>(sp => sp.GetRequiredService<EfReleaseCalendarRepository>());
        services.AddScoped<EfLabelRepository>();
        services.AddScoped<ILabelRepository>(sp => sp.GetRequiredService<EfLabelRepository>());
        services.AddScoped<EfEntityAssetRepository>();
        services.AddScoped<IEntityAssetRepository>(sp => sp.GetRequiredService<EfEntityAssetRepository>());
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository>();
        services.AddScoped<ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository>(
            sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository>());
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeEntityAssetAccessPolicy>();
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.Services.LegalEntityEntityAssetAccessPolicy>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.Documents.IEmployeeDocumentAccess,
            ONEVO.Application.Features.CoreHr.Employee.Documents.EmployeeDocumentAccess>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.Access.IEmployeeViewAccess,
            ONEVO.Application.Features.CoreHr.Employee.Access.EmployeeViewAccess>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.Employee.Access.IEmployeeInvitationAccess,
            ONEVO.Application.Features.CoreHr.Employee.Access.EmployeeInvitationAccess>();
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeDocumentAccessPolicy>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces.IPrimaryEntityAssetLinker,
            ONEVO.Application.Features.Storage.EntityAssets.Services.PrimaryEntityAssetLinker>();
        services.AddScoped<ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces.IEntityAssetAccessPolicyResolver>(sp =>
            new ONEVO.Application.Features.Storage.EntityAssets.Services.EntityAssetAccessPolicyResolver(
                new Dictionary<string, ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces.IEntityAssetAccessPolicy>
                {
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.Employee] =
                        sp.GetRequiredService<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeEntityAssetAccessPolicy>(),
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.LegalEntity] =
                        sp.GetRequiredService<ONEVO.Application.Features.Storage.EntityAssets.Services.LegalEntityEntityAssetAccessPolicy>(),
                    [ONEVO.Application.Common.Constants.EntityAssetOwnerTypes.EmployeeDocument] =
                        sp.GetRequiredService<ONEVO.Application.Features.Storage.EntityAssets.Services.EmployeeDocumentAccessPolicy>()
                    // Part 2 plan (Project/Objective/Task endpoint consolidation) adds the remaining
                    // three owner types here when their controllers migrate to the generic resolve endpoint.
                }));

        // Work Management - Milestone & Achievement services
        services.AddScoped<IMilestoneMembershipCoordinator, MilestoneMembershipCoordinator>();
        services.AddScoped<IWorkHierarchyService, WorkHierarchyService>();
        services.AddScoped<IModuleReadAccess, ModuleReadAccess>();
        services.AddScoped<IWorkCalendarResolver, WorkCalendarResolver>();
        services.AddScoped<IProjectMonitorSnapshotLoader, ProjectMonitorSnapshotLoader>();
        services.AddScoped<IProjectMonitorCallerResolver, ProjectMonitorCallerResolver>();
        services.AddScoped<IProjectMonitorService, ProjectMonitorService>();
        services.AddScoped<IWorkNotificationEngine, WorkNotificationEngine>();
        services.AddScoped<IWorkApprovalEngine, WorkApprovalEngine>();
        services.AddScoped<IApprovalActionApplierRegistry, ApprovalActionApplierRegistry>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Approvals.Services.ApprovalFeedBuilder>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Inbox.MonitorAlertAudience>();
        // Work Management aligned to the Inbox standard (plan W1).
        foreach (var kind in ONEVO.Application.Features.WorkManagement.Inbox.WorkInboxKinds.Descriptors)
            services.AddInboxKind(kind);
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Inbox.WorkAlertEscalation>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationSweepSource, ONEVO.Application.Features.WorkManagement.Inbox.WorkMonitorAlertSweepSource>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Inbox.WorkInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider>(sp => sp.GetRequiredService<ONEVO.Application.Features.WorkManagement.Inbox.WorkInboxSourceProvider>());
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.WorkManagement.Inbox.WorkConversationSubjectResolver>();

        // Conversations (inbox item group chat) - shared module; sources plug in IConversationSubjectResolver.
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.Conversations.EfConversationRepository>();
        services.AddScoped<ONEVO.Application.Features.Conversations.RepositoryInterfaces.IConversationRepository>(sp =>
            sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.Conversations.EfConversationRepository>());
        services.AddScoped<ONEVO.Application.Features.Conversations.RepositoryInterfaces.IConversationPeopleDirectory,
            ONEVO.Infrastructure.Persistence.Repositories.Conversations.EfConversationPeopleDirectory>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.IConversationAccess,
            ONEVO.Application.Features.Conversations.Services.ConversationAccess>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.ConversationResponseBuilder>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.ConversationNotifier>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.IConversationUnreadCounts,
            ONEVO.Application.Features.Conversations.Services.ConversationUnreadCounts>();
        // Inbox router: fans out to every registered IInboxSourceProvider (one per module).
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxRouter, ONEVO.Application.Features.Inbox.Services.InboxRouter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.InboxCallerResolver>();
        // Inbox Standard: kind descriptors (each module adds its own with services.AddInboxKind), the decision
        // journal, the merged Steps timeline. Reverters register as IInboxDecisionReverter per (source, kind).
        services.AddSingleton<ONEVO.Application.Features.Inbox.Contracts.IInboxKindRegistry, ONEVO.Application.Features.Inbox.Contracts.InboxKindRegistry>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxDecisionJournal, ONEVO.Infrastructure.Persistence.Repositories.Inbox.EfInboxDecisionJournal>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxStepsBuilder, ONEVO.Application.Features.Inbox.Services.InboxStepsBuilder>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxChangeNotifier, ONEVO.Application.Features.Inbox.Services.OutboxInboxChangeNotifier>();
        // Inbox escalation standard: one policy registry (first match wins), the shared escalation recorder, and the
        // hourly SLA sweep over every module's IInboxEscalationSweepSource.
        services.AddScoped<ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.IEmployeeAuthorityResolverFactory,
            ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.EmployeeAuthorityResolverFactory>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicy>(sp =>
            new ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ReportingLineThenHrPolicy(
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.IEmployeeAuthorityResolverFactory>().CreateForTenant,
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>(),
                sp.GetRequiredService<ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces.IPermissionRepository>(),
                sp.GetRequiredService<IDateTimeProvider>()));
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicy>(sp =>
            new ONEVO.Application.Features.Calendar.Inbox.ReportingLineThenLogPolicy(
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.IEmployeeAuthorityResolverFactory>().CreateForTenant,
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>()));
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicy, ONEVO.Application.Features.WorkManagement.Inbox.WmHierarchyPolicy>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicyRegistry, ONEVO.Application.Features.Inbox.Escalation.InboxEscalationPolicyRegistry>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationRecorder, ONEVO.Application.Features.Inbox.Escalation.InboxEscalationRecorder>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationSweepSource, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionEscalationSweepSource>();
        // The Api host registers the SignalR-backed publisher; this is the fallback everywhere else.
        services.TryAddSingleton<ONEVO.Application.Features.Conversations.Contracts.IInboxRealtimePublisher,
            ONEVO.Application.Features.Conversations.Services.NullInboxRealtimePublisher>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Tasks.Appliers.TaskCreateApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Meetings.Appliers.MeetingCreateApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Meetings.Appliers.MeetingMoveApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Tasks.Appliers.TaskEditApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Tasks.Appliers.TaskDeleteApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Tasks.Appliers.TaskStatusTemplateChangeApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleAllocationExtendApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleMemberAddApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleMemberRemoveApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleUnachieveApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleAchieveApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleTransferApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleDeleteApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Objectives.Appliers.ModuleEditApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Sprints.Appliers.SprintDeleteApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Sprints.Appliers.SprintAchieveApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Sprints.Appliers.SprintCompleteApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Sprints.Appliers.SprintStartApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Sprints.Appliers.SprintEditApplier>();
        services.AddScoped<IApprovalActionApplier, ONEVO.Application.Features.WorkManagement.Sprints.Appliers.SprintCreateApplier>();

        services.AddScoped<IApprovalActionReverterRegistry, ApprovalActionReverterRegistry>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.TaskCreateReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.TaskEditReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.TaskDeleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Tasks.Reverters.ProjectStatusTemplateChangeReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleAllocationExtendReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleMemberAddReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleMemberRemoveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleUnachieveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleAchieveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleTransferReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleDeleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Objectives.Reverters.ModuleEditReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintDeleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintAchieveReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintCompleteReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintStartReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintEditReverter>();
        services.AddScoped<IApprovalActionReverter, ONEVO.Application.Features.WorkManagement.Sprints.Reverters.SprintCreateReverter>();

        services.AddScoped<ISprintAudienceResolver, SprintAudienceResolver>();
        services.AddScoped<ISprintTaskAssignmentService, SprintTaskAssignmentService>();
        services.AddScoped<ICallerIdentityResolver, CallerIdentityResolver>();
        services.AddScoped<IObjectiveAllocationSlackCalculator, ObjectiveAllocationSlackCalculator>();
        services.AddScoped<ITaskAssetLinker, TaskAssetLinker>();
        services.AddScoped<ITaskWriteService, TaskWriteService>();
        services.AddScoped<IModuleWriteService, ModuleWriteService>();
        services.AddScoped<IModuleActionSubmitter, ModuleActionSubmitter>();
        services.AddScoped<ISprintWriteService, SprintWriteService>();
        services.AddScoped<ISprintActionSubmitter, SprintActionSubmitter>();
        services.AddScoped<ITaskAccessResolver, TaskAccessResolver>();

        // Auth: global email directory
        services.AddScoped<IGlobalEmailDirectoryRepository, EfGlobalEmailDirectoryRepository>();

        // Storage quota (Phase 1 tenant_storage_stats + enforcement service)
        services.AddScoped<
            ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces.ITenantStorageStatsRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota.EfTenantStorageStatsRepository>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IStorageQuotaService,
            ONEVO.Infrastructure.Services.Storage.Quota.StorageQuotaService>();

        // File Storage (Phase 1 file_records + file_upload_reservations)
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.RepositoryInterfaces.IFileRecordRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Storage.File.EfFileRecordRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.RepositoryInterfaces.IFileUploadReservationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Storage.File.EfFileUploadReservationRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IUploadPurposePolicy,
            ONEVO.Infrastructure.Services.Storage.File.UploadPurposePolicy>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IAvatarImageProcessor,
            ONEVO.Infrastructure.Services.Storage.File.AvatarImageProcessor>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IObjectStorageAdapter,
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.CloudflareR2ObjectStorageAdapter>();
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IFileStorageService,
            ONEVO.Infrastructure.Services.Storage.File.FileStorageService>();
        // SEC-13: no real scanner is deployed in any environment yet - see NullMalwareScanner's
        // doc comment. Not wired into the upload/read path until a scanner is selected (decision
        // gate in docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md).
        services.AddScoped<
            ONEVO.Application.Features.Storage.File.ServiceInterfaces.IMalwareScanner,
            ONEVO.Infrastructure.Services.Storage.File.NullMalwareScanner>();

        // System Config - Payment Gateway (Phase 1 canonical tables)
        services.AddScoped<IPaymentGatewayRepository, EfPaymentGatewayRepository>();
        services.AddScoped<IPaymentGatewayVerificationService, PaymentGatewayVerificationService>();
        services.AddScoped<
            IPaymentGatewayWebhookSecretResolver,
            PaymentGatewayWebhookSecretResolver>();

        // System Config - Platform Service Keys (Phase 1 canonical table)
        services.AddScoped<IPlatformServiceKeyRepository, EfPlatformServiceKeyRepository>();
        services.AddScoped<ITrayAppReleaseRepository, EfTrayAppReleaseRepository>();
        services.AddScoped<IPlatformServiceKeyVerificationService, PlatformServiceKeyVerificationService>();
        services.AddScoped<
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.IAwsRekognitionConnectionProbe,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.AwsRekognitionConnectionProbe>();
        services.AddScoped<
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.ICloudflareR2ConnectionProbe,
            ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.CloudflareR2ConnectionProbe>();
        services.AddScoped<IPlatformServiceKeyResolver, PlatformServiceKeyResolver>();

        // System Config - metadata-only provider catalog
        services.AddScoped<IPlatformProviderRepository, EfPlatformProviderRepository>();

        // System Config - Platform OAuth Apps (Phase 1 canonical tables)
        services.AddScoped<IPlatformOAuthAppRepository, EfPlatformOAuthAppRepository>();
        services.AddScoped<IPlatformOAuthAppResolver, PlatformOAuthAppResolver>();

        // System Config - Integration Catalog (Phase 1 canonical tables)
        services.AddScoped<IIntegrationCatalogRepository, EfIntegrationCatalogRepository>();

        // System Config - Configuration Templates (Phase 1 canonical tables)
        services.AddScoped<IConfigurationTemplateRepository, EfConfigurationTemplateRepository>();
        services.AddScoped<ITenantConfigurationTemplateApplicationRepository, EfTenantConfigurationTemplateApplicationRepository>();

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
        services.AddHostedService<ONEVO.Infrastructure.Services.Calendar.CalendarEventReminderJob>();
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
        services.AddHostedService<ONEVO.Infrastructure.Services.Calendar.CalendarIncidentJob>();
        services.AddScoped<ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource, ONEVO.Application.Features.Dashboard.Team.Sources.CalendarMeetingExceptionTeamActionSource>();

        // Tenant cache invalidation
        services.AddScoped<ITenantCacheInvalidator, TenantCacheInvalidator>();

        // Transactional outbox + idempotency
        services.AddScoped<IOutboxWriter, Services.SharedPlatform.Outbox.OutboxWriter>();
        // One instance at a time for singleton background jobs (SCL-01); the instance id is fixed per process.
        services.AddSingleton<ONEVO.Application.Common.ServiceInterfaces.IJobLeaseService, Services.SharedPlatform.Jobs.JobLeaseService>();
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
        services.AddHostedService<Services.SharedPlatform.Outbox.OutboxProcessor>();
        services.AddHostedService<Services.CoreHr.BulkOnboarding.BulkOnboardingBatchProcessor>();
        services.AddHostedService<Services.Auth.Login.LoginWorkspaceSelectionChallengeCleanupService>();

        // Provisioning services
        services.AddScoped<ITenantOwnerInvitationService, TenantOwnerInvitationService>();
        services.AddScoped<IModuleEntitlementService, ModuleEntitlementService>();
        services.AddScoped<IModuleCatalogService, ModuleCatalogService>();
        services.AddScoped<ITenantPermissionCatalogService, TenantPermissionCatalogService>();
        services.AddScoped<IDefaultRoleSeeder, DefaultRoleSeeder>();

        // Provisioning section readers, backed by the data CreateTenantCommandHandler
        // seeds at tenant creation (tenant_subscriptions, its selected_modules_json,
        // tenant.settings_json, and the seeded system roles).
        services.AddScoped<ITenantSubscriptionStatusReader, TenantSubscriptionStatusReader>();
        services.AddScoped<ITenantModuleStatusReader, TenantModuleStatusReader>();
        services.AddScoped<ITenantRoleStatusReader, TenantRoleStatusReader>();
        services.AddScoped<ITenantSettingsStatusReader, TenantSettingsStatusReader>();

        services.AddHttpContextAccessor();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<ICurrentPlatformUserContext, CurrentPlatformUserContext>();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // Monitoring - Tray App Activation
        services.AddScoped<ITrayActivationRepository, EfTrayActivationRepository>();
        services.AddScoped<IDeviceChangeRequestRepository, EfDeviceChangeRequestRepository>();
        services.AddSingleton<ITrayTokenService, TrayTokenService>();
        services.AddScoped<IEmployeeOnlineStatusReader, EmployeeOnlineStatusReader>();

        // Monitoring - Check-In
        services.AddScoped<ICheckInRepository, EfCheckInRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces.IFaceVerificationAttemptRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn.EfFaceVerificationAttemptRepository>();
        services.AddScoped<ITrayCurrentDevice, TrayCurrentDeviceService>();
        services.AddScoped<ITrayEmployeeIdentityResolver, TrayEmployeeIdentityResolver>();

        // Monitoring - Work Sessions (clock-in/break/clock-out)
        services.AddScoped<IWorkSessionRepository, EfWorkSessionRepository>();

        // Monitoring - Activity (keyboard/mouse tracking)
        services.AddScoped<IActivitySnapshotRepository, EfActivitySnapshotRepository>();
        services.AddScoped<IActivityRawBufferRepository, EfActivityRawBufferRepository>();
        services.AddScoped<IAppUsageSnapshotRepository, EfAppUsageSnapshotRepository>();
        services.AddScoped<
            ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces.IGlobalApplicationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.AppCatalog.EfGlobalApplicationRepository>();
        services.AddScoped<IDeviceStateSnapshotRepository, EfDeviceStateSnapshotRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces.INotificationRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Notifications.EfNotificationRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces.IMeetingSignalRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings.EfMeetingSignalRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces.IMeetingComplianceReadRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings.EfMeetingComplianceReadRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces.IBiometricEnrollmentAttemptRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics.EfBiometricEnrollmentAttemptRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces.IBiometricProfileRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics.EfBiometricProfileRepository>();
        services.AddScoped<
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.IAwsRekognitionClientFactory,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.AwsRekognitionClientFactory>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IFaceLivenessService,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.RekognitionFaceLivenessService>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IFaceMatchService,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.RekognitionFaceMatchService>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IFaceQualityService,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.RekognitionFaceQualityService>();
        services.AddScoped<IActivityDailySummaryRepository, EfActivityDailySummaryRepository>();
        services.AddScoped<IActivityLiveDaySummary, ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring.ActivityLiveDaySummary>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces.IProductivityReportRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Reports.EfProductivityReportRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces.IExceptionRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Exceptions.EfExceptionRepository>();
        services.AddScoped<IMonitoringToggleResolver, MonitoringToggleResolverService>();
        services.AddScoped<
            ONEVO.Application.Features.OrgStructure.Services.IPositionPeopleChartReader,
            ONEVO.Infrastructure.Services.OrgStructure.PositionPeopleChartReader>();
        services.AddScoped<
            ONEVO.Application.Features.OrgStructure.Services.IReportingLineMoveService,
            ONEVO.Infrastructure.Services.OrgStructure.ReportingLineMoveService>();
        services.AddScoped<
            ONEVO.Application.Features.CoreHr.OnboardingDraft.Services.IOnboardingWizardLookupService,
            ONEVO.Infrastructure.Services.CoreHr.OnboardingWizardLookupService>();
        services.AddHostedService<ActivityDailySummaryJob>();
        services.AddHostedService<ONEVO.Infrastructure.Services.Monitoring.Exceptions.ExceptionDetectionJob>();
        services.AddHostedService<ONEVO.Infrastructure.Services.Inbox.InboxEscalationSweepJob>();
        services.AddHostedService<ONEVO.Infrastructure.Services.TimeAttendance.AttendanceAlertMaterializerJob>();
        services.AddScoped<ONEVO.Application.Features.Monitoring.Exceptions.Services.CameraVerificationSkippedDetector>();

        // Monitoring - Settings (tenant-level feature toggles admin CRUD)
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces.IMonitoringFeatureTogglesRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Settings.EfMonitoringFeatureTogglesRepository>();
        services.AddHostedService<
            ONEVO.Infrastructure.Services.Monitoring.Notifications.WellnessRuleEvaluatorJob>();
        services.AddHostedService<
            ONEVO.Infrastructure.Services.Monitoring.Notifications.LocationRuleEvaluatorJob>();

        // Monitoring - Screenshots
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces.IEvidenceAssetRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots.EfEvidenceAssetRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces.IAgentCommandRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots.EfAgentCommandRepository>();
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces.IInactivityCaptureAttemptRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots.EfInactivityCaptureAttemptRepository>();
        services.AddHostedService<ONEVO.Infrastructure.Services.Monitoring.Screenshots.AgentCommandExpiryJob>();
        services.AddHostedService<Services.WorkManagement.ProjectMonitorJob>();
        services.AddHostedService<Services.Calendar.CalendarSyncJob>();
        services.AddHostedService<Services.Calendar.TeamsAttendanceSyncJob>();
        services.AddHostedService<Services.Calendar.ZoomAttendanceSyncJob>();

        // Auth services
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        // Runtime MFA challenge storage is always the PostgreSQL-backed mfa_challenges table.
        services.AddScoped<IMfaChallengeStore, PostgresMfaChallengeStore>();
        // Admin/platform MFA challenge storage — in-memory for now (v1), no PostgreSQL-backed
        // equivalent yet. Fine for a single-instance local/dev deployment; revisit before running
        // multiple API instances behind a load balancer.
        services.AddScoped<IPlatformMfaChallengeStore, MemoryPlatformMfaChallengeStore>();
        services.AddScoped<IBaseLoginCandidateRepository, EfBaseLoginCandidateRepository>();
        services.AddScoped<ILoginWorkspaceSelectionChallengeRepository, EfLoginWorkspaceSelectionChallengeRepository>();
        services.AddScoped<IBaseLoginFixedWorkVerifier, BaseLoginFixedWorkVerifier>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<ILoginWorkspaceSelectionChallengeCleanupRunner, LoginWorkspaceSelectionChallengeCleanupRunner>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddSingleton<IPermissionVersionService, PermissionVersionService>();
        services.AddScoped<ITotpService, OtpNetTotpService>();
        services.AddScoped<ILoginSessionMaterialFactory, LoginSessionMaterialFactory>();
        services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
        services.AddScoped<ILoginContinuationService, LoginContinuationService>();
        services.AddScoped<ITenantSessionExchangeChallengeRepository, EfTenantSessionExchangeChallengeRepository>();
        services.AddScoped<ITenantSessionExchangeService, TenantSessionExchangeService>();

        // Legal acceptance and versioning
        services.AddScoped<ILegalDocumentVersionRepository, EfLegalDocumentVersionRepository>();
        services.AddScoped<ILegalAcceptanceRepository, EfLegalAcceptanceRepository>();
        services.AddScoped<ILegalAcceptanceChecker, LegalAcceptanceChecker>();
        services.AddScoped<ILegalAcceptanceSubmissionService, LegalAcceptanceSubmissionService>();
        services.AddScoped<ILegalLoginChallengeRepository, EfLegalLoginChallengeRepository>();

        // Developer Platform access (canonical platform_* tables)
        services.AddScoped<IPlatformUserRepository, EfPlatformUserRepository>();
        services.AddScoped<IPlatformUserCredentialRepository, EfPlatformUserCredentialRepository>();
        services.AddScoped<IPlatformRoleRepository, EfPlatformRoleRepository>();
        services.AddScoped<IPlatformUserSessionRepository, EfPlatformUserSessionRepository>();
        services.AddScoped<IPlatformAccessReadRepository, EfPlatformAccessReadRepository>();
        services.AddScoped<IPlatformAuthEventRepository, EfPlatformAuthEventRepository>();
        services.AddScoped<IPlatformUserInviteRepository, EfPlatformUserInviteRepository>();
        services.AddScoped<IPlatformPermissionResolver, PlatformPermissionResolver>();
        services.AddScoped<IPlatformAccessManagementService, PlatformAccessManagementService>();
        services.AddScoped<IUserExternalIdentityRepository, EfUserExternalIdentityRepository>();
        services.AddScoped<ITenantAuthPolicyRepository, EfTenantAuthPolicyRepository>();

        // -- Platform options binding ------------------------------------------
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<PlatformUrlsOptions>(configuration.GetSection(PlatformUrlsOptions.SectionName));
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Configuration.StorageQuotaOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Configuration.StorageQuotaOptions.SectionName));
        services.Configure<ONEVO.Infrastructure.Configuration.FileStorageOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Configuration.FileStorageOptions.SectionName));
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

        services.AddScoped<IEncryptionService, AesEncryptionService>();

        // -- My Team dashboard: Approvals & Exceptions action sources (spec §8.2/§9.4) --
        // One AddScoped<ITeamActionSource, ...> per source key, same pattern as
        // IEmailProviderAdapter above - GetTeamActionItemsQueryHandler resolves all of them via
        // IEnumerable<ITeamActionSource>.
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.LeaveApprovalTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.AttendanceCorrectionTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.AttendanceWorkAreaChangeTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.AttendanceLocationChangeTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.AttendanceDeviceChangeTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.MonitoringExceptionTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.WorkTaskCreationTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.WorkTaskEditTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.WorkObjectiveChangeTeamActionSource>();
        services.AddScoped<
            ONEVO.Application.Features.Dashboard.Team.Abstractions.ITeamActionSource,
            ONEVO.Application.Features.Dashboard.Team.Sources.WorkStatusTemplateChangeTeamActionSource>();

        // Background seeder
        services.AddHostedService<PermissionSeeder>();
        services.AddHostedService<RoleTemplateSeeder>();
        services.AddHostedService<LookupDataSeeder>();
        services.AddHostedService<NotificationTemplateSeeder>();
        services.AddHostedService<Services.SharedPlatform.Notifications.NotificationTrayExpiryJob>();
        services.AddHostedService<PlatformAccessSeeder>();
        services.AddHostedService<PositionTemplatePackSeeder>();
        services.AddHostedService<ModuleCatalogSeeder>();
        services.AddHostedService<DevSmokeTestTenantSeeder>();
        services.AddHostedService<WorkManagementDapiDemoSeeder>();
        services.AddHostedService<DapiOrgStructureSeeder>();
        services.AddHostedService<DapiLeaveSampleSeeder>();
        services.AddHostedService<DapiAttendanceDemoSeeder>();
        services.AddHostedService<DapiHistorySeeder>();
        services.AddHostedService<PlatformOAuthProviderMetadataSeeder>();
        services.AddHostedService<WorkManagementSampleDataSeeder>();
        services.AddHostedService<DapiDueTodayTaskSeeder>();

        // Boot-time configuration audit (warns about missing keys; never fatal).
        services.AddHostedService<ConfigurationStartupValidator>();

        return services;
    }
}
