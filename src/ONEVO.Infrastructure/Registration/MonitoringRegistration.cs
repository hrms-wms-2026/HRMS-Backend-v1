using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.TrayActivation;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.WorkSessions;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Services.Monitoring.CheckIn;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Tray activation, check-in, work sessions, activity, biometrics, exceptions, settings, screenshots and retention.</summary>
internal static class MonitoringRegistration
{
    internal static IServiceCollection AddMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces.IMonitoringPolicyConfigurationService,
            ONEVO.Infrastructure.Services.Monitoring.Settings.MonitoringPolicyConfigurationService>();

        // Monitoring - Tray App Activation
        services.AddScoped<ITrayActivationRepository, EfTrayActivationRepository>();
        services.AddScoped<IDeviceChangeRequestRepository, EfDeviceChangeRequestRepository>();
        services.AddTrayJwtSigning();
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
            ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces.IMonitoringPermissionConsentRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.PermissionConsent.EfMonitoringPermissionConsentRepository>();
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

        services.AddScoped<ONEVO.Application.Features.Monitoring.Exceptions.Services.CameraVerificationSkippedDetector>();

        // Monitoring - Settings (tenant-level feature toggles admin CRUD)
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces.IMonitoringFeatureTogglesRepository,
            ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Settings.EfMonitoringFeatureTogglesRepository>();

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

        // Monitoring - Retention (MON-02: purges activity_raw_buffer and expired screenshot evidence)
        services.AddScoped<
            ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces.IMonitoringRetentionPurgeRunner,
            ONEVO.Infrastructure.Services.Monitoring.Retention.MonitoringRetentionPurgeRunner>();

        return services;
    }
}
