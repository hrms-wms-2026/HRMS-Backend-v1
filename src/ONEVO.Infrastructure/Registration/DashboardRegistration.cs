using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Dashboard layouts and the My Team action sources.</summary>
internal static class DashboardRegistration
{
    internal static IServiceCollection AddDashboard(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces.IUserDashboardRepository, ONEVO.Infrastructure.Persistence.Repositories.Dashboard.EfUserDashboardRepository>();
        services.AddScoped<ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces.IDashboardTemplateRepository, ONEVO.Infrastructure.Persistence.Repositories.Dashboard.EfDashboardTemplateRepository>();

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

        return services;
    }
}
