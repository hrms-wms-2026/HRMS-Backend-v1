namespace ONEVO.Api.Extensions;

internal static class CorsExtensions
{
    internal static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCors(options =>
            options.AddPolicy("AllowFrontend", policy =>
                policy.WithOrigins(configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()));

        return services;
    }
}
