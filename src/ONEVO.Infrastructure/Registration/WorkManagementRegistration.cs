using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Projects, objectives, tasks, meetings, planner, sprints, approvals (appliers and reverters) and leadership.</summary>
internal static class WorkManagementRegistration
{
    internal static IServiceCollection AddWorkManagement(this IServiceCollection services, IConfiguration configuration)
    {

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
        services.Configure<ONEVO.Application.Features.WorkManagement.Planner.PlannerOptions>(
            configuration.GetSection(ONEVO.Application.Features.WorkManagement.Planner.PlannerOptions.SectionName));
        services.AddOptions<ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions>()
            .Configure(options => ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions.BindFrom(
                configuration.GetSection(ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions.Section), options))
            .ValidateOnStart();

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
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.IMeetingApprovalSnapshotBuilder,
            ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingApprovalSnapshotBuilder>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingMoveService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.SeriesMeetingLinkService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingOccurrenceGenerator>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingHoursStamper>();
        services.AddScoped<ONEVO.Application.Features.Calendar.Services.CalendarMeetingGuard>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces.IPlannerRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner.EfPlannerRepository>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces.ITeamTimeReadRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner.EfTeamTimeReadRepository>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Planner.Services.PlannerService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Planner.Services.TeamPlannerService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Planner.Services.IPlannerBlockWriter>(
            sp => sp.GetRequiredService<ONEVO.Application.Features.WorkManagement.Planner.Services.PlannerService>());
        services.AddScoped<ONEVO.Application.Features.WorkManagement.WeeklyProgress.RepositoryInterfaces.IWeeklyWorkProgressReadRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.WeeklyProgress.EfWeeklyWorkProgressReadRepository>();

        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingAttendanceSyncService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingAttendanceReadService>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingCalendarProjection>();
        services.AddScoped<ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts.IParticipantAvailabilityChecker, ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts.ParticipantAvailabilityChecker>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingAttendanceRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings.EfMeetingAttendanceRepository>();

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
        // Action items (Plan D, spec 5.4): the link between a meeting occurrence and the ordinary task raised from it.
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces.IMeetingActionItemRepository,
            ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings.EfMeetingActionItemRepository>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingActionItemService>();
        // Bulk invite preview (Plan D, spec 5.2 / D8): a department or a manager's direct reports expanded into a snapshot.
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingBulkInviteService>();
        // Reporting-line attendance view (Plan D, spec D16 / D17): the employee's own attendance summary, seen by
        // the employee and by their managers in the current reporting line.
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Meetings.Services.EmployeeMeetingAttendanceService>();

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
        services.AddScoped<ITaskRateSettingRepository, EfTaskRateSettingRepository>();
        services.AddScoped<IModuleHourSummaryRepository, EfModuleHourSummaryRepository>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces.ILedProgressReadRepository, EfLedProgressReadRepository>();

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

        // Work Management - Milestone & Achievement services
        services.AddScoped<IMilestoneMembershipCoordinator, MilestoneMembershipCoordinator>();
        services.AddScoped<IWorkHierarchyService, WorkHierarchyService>();
        services.AddScoped<IModuleReadAccess, ModuleReadAccess>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Objectives.Services.ObjectiveAssetAccess>();
        services.AddScoped<IWorkCalendarResolver, WorkCalendarResolver>();
        services.AddScoped<IConversionRateReader, ConversionRateReader>();
        services.AddScoped<IProjectMonitorSnapshotLoader, ProjectMonitorSnapshotLoader>();
        services.AddScoped<IProjectMonitorCallerResolver, ProjectMonitorCallerResolver>();
        services.AddScoped<IProjectMonitorService, ProjectMonitorService>();
        services.AddScoped<IWorkNotificationEngine, WorkNotificationEngine>();
        services.AddScoped<IWorkApprovalEngine, WorkApprovalEngine>();
        services.AddScoped<IApprovalActionApplierRegistry, ApprovalActionApplierRegistry>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Approvals.Services.ApprovalFeedBuilder>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Inbox.MonitorAlertAudience>();

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

        return services;
    }
}
