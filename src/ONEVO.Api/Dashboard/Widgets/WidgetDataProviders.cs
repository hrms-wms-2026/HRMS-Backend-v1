using MediatR;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Api.Controllers.Tenant.Monitoring.ActivityMonitoring;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyFocusStatus;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.Team.Queries;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyActiveTasks;

namespace ONEVO.Api.Dashboard.Widgets;

/// <summary>my-tasks: mirrors GET api/v1/work/my-tasks (TasksController.MyTasks).</summary>
public sealed class MyTasksWidgetDataProvider(IMediator mediator, ICurrentUser currentUser, IModuleEntitlementService entitlements)
    : EndpointMirrorWidgetDataProvider(mediator, currentUser, entitlements)
{
    public const string Key = "my-tasks";

    /// <summary>MyTasksApiService.getMyTasks() default; my-tasks-card passes no argument.</summary>
    public const int UpcomingDays = 7;

    private static readonly EndpointGate MirroredGate = EndpointGate.For<TasksController>(nameof(TasksController.MyTasks));

    public override string WidgetKey => Key;

    protected override EndpointGate Gate => MirroredGate;

    protected override async Task<Result<object>> LoadAsync(CancellationToken ct) =>
        Map(await Mediator.Send(new GetMyActiveTasksQuery(UpcomingDays), ct), value => value.ToViewModel());
}

/// <summary>team-progress: mirrors GET api/v1/work/led-progress (WorkLeadershipController.LedProgress).</summary>
public sealed class TeamProgressWidgetDataProvider(IMediator mediator, ICurrentUser currentUser, IModuleEntitlementService entitlements)
    : EndpointMirrorWidgetDataProvider(mediator, currentUser, entitlements)
{
    public const string Key = "team-progress";

    /// <summary>MyTeamLedProgressApiService.getLedProgress() default; the store passes no argument.</summary>
    public const int OverdueLimit = 10;

    private static readonly EndpointGate MirroredGate =
        EndpointGate.For<WorkLeadershipController>(nameof(WorkLeadershipController.LedProgress));

    public override string WidgetKey => Key;

    protected override EndpointGate Gate => MirroredGate;

    // The action returns Ok(result.Value) - no view-model mapping.
    protected override async Task<Result<object>> LoadAsync(CancellationToken ct) =>
        Map(await Mediator.Send(new GetLedWorkProgressQuery(OverdueLimit), ct), value => value);
}

/// <summary>team-status: mirrors GET api/v1/attendance/time-tracking/team/today (TimeTrackingController.TeamToday).</summary>
public sealed class TeamStatusWidgetDataProvider(IMediator mediator, ICurrentUser currentUser, IModuleEntitlementService entitlements)
    : EndpointMirrorWidgetDataProvider(mediator, currentUser, entitlements)
{
    public const string Key = "team-status";

    /// <summary>MyTeamTodayApiService.getTeamToday() default; the store passes no argument.</summary>
    public const int Limit = 50;

    private static readonly EndpointGate MirroredGate =
        EndpointGate.For<TimeTrackingController>(nameof(TimeTrackingController.TeamToday));

    public override string WidgetKey => Key;

    protected override EndpointGate Gate => MirroredGate;

    // The action returns Ok(result.Value) - no view-model mapping.
    protected override async Task<Result<object>> LoadAsync(CancellationToken ct) =>
        Map(await Mediator.Send(new GetCoveredTeamTodayQuery(Limit), ct), value => value);
}

/// <summary>team-attendance: mirrors GET api/v1/attendance/time-tracking/team/attendance-summary
/// (TimeTrackingController.TeamAttendanceSummary) with the card's first request: this week, no department.</summary>
public sealed class TeamAttendanceWidgetDataProvider(IMediator mediator, ICurrentUser currentUser, IModuleEntitlementService entitlements)
    : EndpointMirrorWidgetDataProvider(mediator, currentUser, entitlements)
{
    public const string Key = "team-attendance";

    private static readonly EndpointGate MirroredGate =
        EndpointGate.For<TimeTrackingController>(nameof(TimeTrackingController.TeamAttendanceSummary));

    public override string WidgetKey => Key;

    protected override EndpointGate Gate => MirroredGate;

    // The action returns Ok(result.Value) - no view-model mapping.
    protected override async Task<Result<object>> LoadAsync(CancellationToken ct) =>
        Map(await Mediator.Send(new GetTeamAttendanceSummaryQuery("week", null, null), ct), value => value);
}

/// <summary>wellbeing: mirrors GET api/v1/monitoring/activity/my-focus-status (MonitoringActivityController.GetMyFocusStatus).</summary>
public sealed class WellbeingWidgetDataProvider(IMediator mediator, ICurrentUser currentUser, IModuleEntitlementService entitlements)
    : EndpointMirrorWidgetDataProvider(mediator, currentUser, entitlements)
{
    public const string Key = "wellbeing";

    private static readonly EndpointGate MirroredGate =
        EndpointGate.For<MonitoringActivityController>(nameof(MonitoringActivityController.GetMyFocusStatus));

    public override string WidgetKey => Key;

    protected override EndpointGate Gate => MirroredGate;

    // The action returns Ok(result.Value) - no view-model mapping.
    protected override async Task<Result<object>> LoadAsync(CancellationToken ct) =>
        Map(await Mediator.Send(new GetMyFocusStatusQuery(), ct), value => value);
}

/// <summary>active-projects: mirrors GET api/v1/work/projects/mine?pageNumber=1&amp;pageSize=100 (ProjectsController.ListMine).</summary>
public sealed class ActiveProjectsWidgetDataProvider(IMediator mediator, ICurrentUser currentUser, IModuleEntitlementService entitlements)
    : EndpointMirrorWidgetDataProvider(mediator, currentUser, entitlements)
{
    public const string Key = "active-projects";

    /// <summary>active-projects-card calls ProjectApiService.listMine(1, PROJECT_PAGE_SIZE = 100).</summary>
    public const int PageNumber = 1;
    public const int PageSize = 100;

    private static readonly EndpointGate MirroredGate = EndpointGate.For<ProjectsController>(nameof(ProjectsController.ListMine));

    public override string WidgetKey => Key;

    protected override EndpointGate Gate => MirroredGate;

    protected override async Task<Result<object>> LoadAsync(CancellationToken ct)
    {
        // Model binding of ?pageNumber=1&pageSize=100 leaves SortBy null and SortDirection "asc" (PagedRequest defaults).
        var paging = new PagedRequest { PageNumber = PageNumber, PageSize = PageSize };
        return Map(await Mediator.Send(new ListProjectsQuery(null, paging), ct), value => value.ToViewModel());
    }
}

public static class WidgetDataProviderRegistration
{
    /// <summary>
    /// Registers each provider as a keyed scoped IWidgetDataProvider (key = widget key). Must match the
    /// registry's HasProvider flags (asserted by a unit test). todays-schedule, today-at-a-glance,
    /// today-total-time and work-pattern stay client-sourced: see DashboardWidgetRegistry.
    /// </summary>
    public static IServiceCollection AddDashboardWidgetDataProviders(this IServiceCollection services)
    {
        Add<MyTasksWidgetDataProvider>(services, MyTasksWidgetDataProvider.Key);
        Add<TeamProgressWidgetDataProvider>(services, TeamProgressWidgetDataProvider.Key);
        Add<TeamStatusWidgetDataProvider>(services, TeamStatusWidgetDataProvider.Key);
        Add<TeamAttendanceWidgetDataProvider>(services, TeamAttendanceWidgetDataProvider.Key);
        Add<WellbeingWidgetDataProvider>(services, WellbeingWidgetDataProvider.Key);
        Add<ActiveProjectsWidgetDataProvider>(services, ActiveProjectsWidgetDataProvider.Key);
        return services;
    }

    private static void Add<TProvider>(IServiceCollection services, string key)
        where TProvider : class, Application.Features.Dashboard.Layout.Data.IWidgetDataProvider =>
        services.AddKeyedScoped<Application.Features.Dashboard.Layout.Data.IWidgetDataProvider, TProvider>(key);
}
