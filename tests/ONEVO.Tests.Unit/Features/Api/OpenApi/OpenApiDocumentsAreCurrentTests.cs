using System.Text.Json;

namespace ONEVO.Tests.Unit.Features.Api.OpenApi;

/// <summary>
/// Task 23: the OpenAPI documents are generated at build time (ONEVO.Api.csproj writes them to
/// src/ONEVO.Api/obj/openapi on every build) and committed in docs/api/openapi for the frontend's
/// TypeScript types and for Postman. This fails when the committed copies drift from what the
/// build produces. To fix it, refresh them and commit:
///   dotnet build src/ONEVO.Api/ONEVO.Api.csproj -p:UpdateOpenApiDocs=true
/// </summary>
public sealed class OpenApiDocumentsAreCurrentTests
{
    public static TheoryData<string, string> Documents => new()
    {
        { "crownus-api.json", "tenant-v1.json" },
        { "crownus-api_admin-v1.json", "admin-v1.json" },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void CommittedDocument_MatchesTheBuildOutput(string generatedName, string committedName)
    {
        var root = RepositoryRoot();
        var generatedPath = Path.Combine(root, "src", "ONEVO.Api", "obj", "openapi", generatedName);
        var committedPath = Path.Combine(root, "docs", "api", "openapi", committedName);

        Assert.True(File.Exists(generatedPath), $"The build did not produce {generatedPath}.");
        Assert.True(File.Exists(committedPath), $"{committedPath} is missing. {RefreshHint}");

        var generated = Normalize(File.ReadAllText(generatedPath));
        var committed = Normalize(File.ReadAllText(committedPath));
        Assert.True(generated == committed, $"docs/api/openapi/{committedName} is out of date. {RefreshHint}");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void CommittedDocument_DescribesEndpointsAndTheSignInSchemes(string generatedName, string committedName)
    {
        Assert.NotEmpty(generatedName);
        var path = Path.Combine(RepositoryRoot(), "docs", "api", "openapi", committedName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.StartsWith("3.", root.GetProperty("openapi").GetString());
        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.NotEmpty(paths);
        var admin = committedName.StartsWith("admin", StringComparison.Ordinal);
        Assert.All(paths, p => Assert.Equal(admin, p.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)));

        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        Assert.True(schemes.TryGetProperty("CsrfToken", out _));
        Assert.True(schemes.TryGetProperty(admin ? "AdminSessionCookie" : "TenantSessionCookie", out _));
        if (!admin)
            Assert.True(schemes.TryGetProperty("TrayDeviceBearer", out _));
    }

    private const string RefreshHint =
        "Run `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -p:UpdateOpenApiDocs=true` and commit docs/api/openapi.";

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api", "ONEVO.Api.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
