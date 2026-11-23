using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Clock-in policies, attendance reads and alerts, corrections, work areas and locations, work modes.</summary>
internal static class TimeAttendanceRegistration
{
    internal static IServiceCollection AddTimeAttendance(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IClockInPolicyRepository, EfClockInPolicyRepository>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Services.IEmployeeClockInPolicyResolver, ONEVO.Application.Features.TimeAttendance.Services.EmployeeClockInPolicyResolver>();
        services.AddScoped<IAttendanceReadRepository, EfAttendanceReadRepository>();
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertRepository>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertRepository>(sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertRepository>());
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertResolutionLookup>(sp => sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertRepository>());
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertMaterializer, ONEVO.Application.Features.TimeAttendance.Alerts.AttendanceAlertMaterializer>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.IAttendanceAlertDeductionRepository, ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfAttendanceAlertDeductionRepository>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Alerts.AttendanceDeductionService>();

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

        services.AddScoped<IExpectedWorkAreaResolver, ExpectedWorkAreaResolver>();

        services.AddScoped<IWorkModeRepository, EfWorkModeRepository>();
        services.AddScoped<IWorkModeSeeder, ONEVO.Infrastructure.Services.TimeAttendance.WorkModeSeeder>();

        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Team.Services.ITeamCompletedTaskReader,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfTeamCompletedTaskReader>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Team.Services.IForecastTaskReader,
            ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance.EfForecastTaskReader>();

        return services;
    }
}
