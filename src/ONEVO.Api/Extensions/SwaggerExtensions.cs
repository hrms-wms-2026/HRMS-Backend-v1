using Microsoft.OpenApi;

namespace ONEVO.Api.Extensions;

internal static class SwaggerExtensions
{
    internal static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "ONEVO Tenant API", Version = "v1" });
            c.SwaggerDoc("admin-v1", new() { Title = "ONEVO Admin API", Version = "admin-v1" });
            c.DocInclusionPredicate((docName, api) =>
            {
                var path = api.RelativePath ?? "";
                return docName == "admin-v1"
                    ? path.StartsWith("admin/", StringComparison.OrdinalIgnoreCase)
                    : !path.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);
            });
            c.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);
            c.AddSecurityDefinition("CsrfToken", new OpenApiSecurityScheme
            {
                Name = "X-CSRF-Token",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "CSRF Token retrieved during login."
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("CsrfToken", document, null),
                    []
                }
            });
        });

        return services;
    }

    internal static WebApplication UseApiSwagger(this WebApplication app)
    {
        var swaggerEnabled = app.Environment.IsDevelopment()
            || app.Configuration.GetValue<bool>("Swagger:Enabled");
        if (!swaggerEnabled) return app;

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "ONEVO Tenant API v1");
            c.SwaggerEndpoint("/swagger/admin-v1/swagger.json", "ONEVO Admin API");
        });

        return app;
    }
}