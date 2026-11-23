using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Departments, positions and the reporting-line services.</summary>
internal static class OrgStructureRegistration
{
    internal static IServiceCollection AddOrgStructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();

        services.AddScoped<IPositionRepository, EfPositionRepository>();

        services.AddScoped<
            ONEVO.Application.Features.OrgStructure.Services.IPositionPeopleChartReader,
            ONEVO.Infrastructure.Services.OrgStructure.PositionPeopleChartReader>();
        services.AddScoped<
            ONEVO.Application.Features.OrgStructure.Services.IReportingLineMoveService,
            ONEVO.Infrastructure.Services.OrgStructure.ReportingLineMoveService>();

        return services;
    }
}
