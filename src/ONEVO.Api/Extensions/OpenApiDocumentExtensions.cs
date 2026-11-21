using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ONEVO.Api.Extensions;

/// <summary>
/// Task 23: the API description is produced at build time only. Swagger UI and every runtime
/// /swagger or /openapi endpoint are gone in all environments; nothing here is ever mapped
/// (no MapOpenApi). Microsoft.Extensions.ApiDescription.Server runs the app up to
/// <c>builder.Build()</c> during <c>dotnet build</c> and writes one document per audience to
/// obj/openapi. The committed copies live in docs/api/openapi (see docs/api/openapi/README.md);
/// OpenApiDocumentsAreCurrentTests fails when they drift from what the build produces.
/// </summary>
internal static class OpenApiDocumentExtensions
{
    /// <summary>Tenant web app and tray API: everything except admin/v1.</summary>
    public const string TenantDocument = "v1";

    /// <summary>Developer platform admin console API: admin/v1 only.</summary>
    public const string AdminDocument = "admin-v1";

    public const string TenantSessionScheme = "TenantSessionCookie";
    public const string AdminSessionScheme = "AdminSessionCookie";
    public const string CsrfScheme = "CsrfToken";
    public const string TrayBearerScheme = "TrayDeviceBearer";

    /// <summary>
    /// True while Microsoft.Extensions.ApiDescription.Server generates the documents at build time.
    /// Program.cs skips the startup work that needs a database or secrets in that mode.
    /// </summary>
    public static bool IsGeneratingDocuments =>
        string.Equals(
            System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name,
            "GetDocument.Insider",
            StringComparison.Ordinal);

    /// <summary>
    /// Placeholder settings used only while generating documents at build time, so service
    /// registration that reads them does not fail. Nothing connects anywhere: the generator stops
    /// the app at builder.Build(), before any hosted service or request runs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> GenerationPlaceholderSettings =
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=openapi-generation.invalid;Database=none;User Id=none;Password=none",
            ["ConnectionStrings:MigrationConnection"] = "Server=openapi-generation.invalid;Database=none;User Id=none;Password=none",
        };

    internal static IServiceCollection AddApiOpenApiDocuments(this IServiceCollection services)
    {
        services.AddOpenApi(TenantDocument, options =>
        {
            options.ShouldInclude = description => !IsAdminRoute(description);
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = "CROWNUS Tenant API", Version = "v1" };
                AddSecuritySchemes(document, admin: false);
                return Task.CompletedTask;
            });
        });

        services.AddOpenApi(AdminDocument, options =>
        {
            options.ShouldInclude = IsAdminRoute;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = "CROWNUS Admin API", Version = "admin-v1" };
                AddSecuritySchemes(document, admin: true);
                return Task.CompletedTask;
            });
        });

        return services;
    }

    private static bool IsAdminRoute(ApiDescription description) =>
        (description.RelativePath ?? string.Empty).StartsWith("admin/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The sign-in methods the API really uses: an HttpOnly session cookie (tenant or admin) plus the
    /// X-CSRF-Token header on unsafe methods, and a bearer token for the tray app (tenant document).
    /// </summary>
    private static void AddSecuritySchemes(OpenApiDocument document, bool admin)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        var schemes = document.Components.SecuritySchemes;

        var sessionScheme = admin ? AdminSessionScheme : TenantSessionScheme;
        schemes[sessionScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = admin ? "admin_session" : ONEVO.Api.Auth.AuthCookieNames.Session,
            Description = "HttpOnly session cookie set by the sign-in endpoint."
        };
        schemes[CsrfScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-CSRF-Token",
            Description = "CSRF token returned at sign-in; required on POST, PUT, PATCH and DELETE."
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(sessionScheme, document, null)] = [],
            [new OpenApiSecuritySchemeReference(CsrfScheme, document, null)] = []
        });

        if (!admin)
        {
            schemes[TrayBearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Tray device access token (Authorization: Bearer {tray_access_token})."
            };
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(TrayBearerScheme, document, null)] = []
            });
        }
    }
}
