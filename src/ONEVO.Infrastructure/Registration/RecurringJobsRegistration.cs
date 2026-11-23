using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Services.Auth.Login;
using ONEVO.Infrastructure.Services.Calendar;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: The job lease, the job runner and every recurring job, in the order they start (task 13: docs/deployment/background-jobs.md). BackgroundJobs:Enabled=false leaves the jobs out.</summary>
internal static class RecurringJobsRegistration
{
    internal static IServiceCollection AddRecurringJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Leave.LeaveCreditJob>(configuration);

        services.AddBackgroundJob<ONEVO.Infrastructure.Services.CoreHr.OfficeProjectReconciliationStartupService>(configuration);

        services.AddHostedService<Services.WorkManagement.Meetings.MeetingsStartupCheck>();

        services.AddBackgroundJob<Services.WorkManagement.Meetings.MeetingOccurrenceGeneratorJob>(configuration);

        services.AddBackgroundJob<Services.WorkManagement.Meetings.MeetingAttendanceJob>(configuration);

        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Calendar.CalendarEventReminderJob>(configuration);

        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Calendar.CalendarIncidentJob>(configuration);

        // One instance at a time for singleton background jobs (SCL-01); the instance id is fixed per process.
        services.AddSingleton<ONEVO.Application.Common.ServiceInterfaces.IJobLeaseService, Services.SharedPlatform.Jobs.JobLeaseService>();
        // Task 13: every recurring job runs under its lease, with a time limit and one log line per run.
        services.AddSingleton<Services.SharedPlatform.Jobs.ExclusiveJobRunner>();

        // Task 31: two handlers for one outbox type stop start-up; runs on web-only instances too.
        services.AddHostedService<Services.SharedPlatform.Outbox.OutboxHandlerStartupCheck>();
        services.AddBackgroundJob<Services.SharedPlatform.Outbox.OutboxProcessor>(configuration);
        services.AddBackgroundJob<Services.SharedPlatform.Outbox.OutboxRetentionPurgeJob>(configuration);
        services.AddBackgroundJob<Services.CoreHr.BulkOnboarding.BulkOnboardingBatchProcessor>(configuration);
        services.AddBackgroundJob<Services.Auth.Login.LoginWorkspaceSelectionChallengeCleanupService>(configuration);

        services.AddBackgroundJob<ActivityDailySummaryJob>(configuration);
        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Monitoring.Exceptions.ExceptionDetectionJob>(configuration);
        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Inbox.InboxEscalationSweepJob>(configuration);
        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Inbox.InboxEntryConsistencyJob>(configuration);
        services.AddBackgroundJob<ONEVO.Infrastructure.Services.TimeAttendance.AttendanceAlertMaterializerJob>(configuration);

        services.AddBackgroundJob<
            ONEVO.Infrastructure.Services.Monitoring.Notifications.WellnessRuleEvaluatorJob>(configuration);
        services.AddBackgroundJob<
            ONEVO.Infrastructure.Services.Monitoring.Notifications.LocationRuleEvaluatorJob>(configuration);

        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Monitoring.Screenshots.AgentCommandExpiryJob>(configuration);

        services.AddBackgroundJob<ONEVO.Infrastructure.Services.Monitoring.Retention.MonitoringRetentionPurgeJob>(configuration);

        services.AddBackgroundJob<Services.WorkManagement.ProjectMonitorJob>(configuration);
        services.AddBackgroundJob<Services.Calendar.CalendarSyncJob>(configuration);
        services.AddBackgroundJob<Services.Calendar.TeamsAttendanceSyncJob>(configuration);
        services.AddBackgroundJob<Services.Calendar.ZoomAttendanceSyncJob>(configuration);

        return services;
    }
}
