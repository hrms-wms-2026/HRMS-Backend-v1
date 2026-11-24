using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: DbContext, its interceptors and the unit of work.</summary>
internal static class DatabaseRegistration
{
    internal static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();
        services.AddScoped<TenantRlsInterceptor>();
        services.AddSingleton<SessionTicketCacheInvalidationInterceptor>();
        services.AddSingleton<ReferenceDataCacheInvalidationInterceptor>(); // task 22
        // Task 06: SQL command count and time per request, read by the request log line.
        services.AddScoped<ONEVO.Infrastructure.Persistence.Diagnostics.RequestSqlMetrics>();
        services.AddScoped<ONEVO.Application.Common.ServiceInterfaces.IRequestSqlMetrics>(sp =>
            sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Diagnostics.RequestSqlMetrics>());
        services.AddScoped<SqlCommandMetricsInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options
                .UseSqlServer(connectionString, sqlServer =>
                {
                    sqlServer.EnableRetryOnFailure(3);
                    sqlServer.CommandTimeout(30);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>())
                .AddInterceptors(sp.GetRequiredService<SessionTicketCacheInvalidationInterceptor>())
                .AddInterceptors(sp.GetRequiredService<ReferenceDataCacheInvalidationInterceptor>())
                .AddInterceptors(sp.GetRequiredService<SqlCommandMetricsInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ONEVO.Application.Common.ServiceInterfaces.IRowVersionStore, Persistence.SqlServer.EfRowVersionStore>(); // task 42

        return services;
    }
}
