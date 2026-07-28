using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Static architecture guarantees for the legal document rich content feature:
/// - The admin list DTO never carries content_json/content_html/content_text.
/// - Create/Update commands never accept a client-supplied content_hash.
/// - PendingLegalDocumentDto exposes content_endpoint (no content_url-only behavior remains
///   for required documents).
/// - The two new controllers define no nested request/response record types.
/// </summary>
public class LegalDocumentRichContentArchitectureTests
{
    [Fact]
    public void SummaryDto_DoesNotExpose_ContentBodyProperties()
    {
        var summaryType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses.LegalDocumentVersionSummaryDto);
        var propertyNames = summaryType.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("ContentJson", propertyNames);
        Assert.DoesNotContain("ContentHtml", propertyNames);
        Assert.DoesNotContain("ContentText", propertyNames);
    }

    [Fact]
    public void CreateRequestDto_NeverExposesContentHash()
    {
        var requestType = typeof(ONEVO.Api.Contracts.Admin.Legal.CreateLegalDocumentVersionRequest);
        var propertyNames = requestType.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("ContentHash", propertyNames);
    }

    [Fact]
    public void UpdateRequestDto_NeverExposesContentHash()
    {
        var requestType = typeof(ONEVO.Api.Contracts.Admin.Legal.UpdateLegalDocumentVersionRequest);
        var propertyNames = requestType.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("ContentHash", propertyNames);
    }

    [Fact]
    public void RichContentMigration_IsSelfContained_DoesNotReferenceApplicationHelpers()
    {
        // Migrations must reproduce the exact data change they recorded at authoring time forever.
        // Referencing Application-layer bootstrap constants/hashers here would let a later rename
        // or wording tweak in that layer silently change what a from-scratch migration replay
        // backfills - so this migration must carry its own literals and hash algorithm.
        var migrationCode = File.ReadAllText(FindMigrationFile("20260728095359_AddLegalDocumentRichContent.cs"));

        Assert.DoesNotContain("using ONEVO.Application", migrationCode);
        Assert.DoesNotContain("LegalDocumentBootstrapContent", migrationCode);
        Assert.DoesNotContain("LegalContentHasher", migrationCode);
    }

    private static string FindMigrationFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{fileName}' above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void CreateCommand_NeverAcceptsClientSuppliedContentHash()
    {
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion.CreateLegalDocumentVersionCommand);
        var constructorParamNames = commandType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name);

        Assert.DoesNotContain("ContentHash", constructorParamNames);
        Assert.DoesNotContain("contentHash", constructorParamNames);
    }

    [Fact]
    public void UpdateCommand_NeverAcceptsClientSuppliedContentHash()
    {
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion.UpdateLegalDocumentVersionCommand);
        var constructorParamNames = commandType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name);

        Assert.DoesNotContain("ContentHash", constructorParamNames);
        Assert.DoesNotContain("contentHash", constructorParamNames);
    }

    [Fact]
    public void UpdateCommand_NeverAcceptsDocumentTypeOrVersion()
    {
        // document_type/version are immutable after create - Update must not be able to change them.
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion.UpdateLegalDocumentVersionCommand);
        var constructorParamNames = commandType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name)
            .ToHashSet();

        Assert.DoesNotContain("DocumentType", constructorParamNames);
        Assert.DoesNotContain("Version", constructorParamNames);
    }

    [Fact]
    public void PendingLegalDocumentDto_ExposesContentEndpoint_NotContentUrlOnly()
    {
        var dtoType = typeof(ONEVO.Application.Features.Auth.Legal.Services.PendingLegalDocumentDto);
        var propertyNames = dtoType.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Contains("ContentEndpoint", propertyNames);
        Assert.Contains("ContentHash", propertyNames);
    }

    [Fact]
    public void AdminController_DefinesNoNestedRequestRecords()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Legal.AdminLegalDocumentVersionsController);
        var nestedTypes = GetHandWrittenNestedTypes(controllerType);

        Assert.Empty(nestedTypes);
    }

    [Fact]
    public void PublicContentController_DefinesNoNestedRequestRecords()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Tenant.Legal.LegalDocumentContentController);
        var nestedTypes = GetHandWrittenNestedTypes(controllerType);

        Assert.Empty(nestedTypes);
    }

    private static Type[] GetHandWrittenNestedTypes(Type controllerType)
    {
        // Excludes compiler-generated async state-machine types (<MethodName>d__N), which are
        // not hand-written nested request/response records.
        return controllerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false))
            .ToArray();
    }

    [Fact]
    public void AdminController_UsesPlatformPermissions_OnEveryAction()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Legal.AdminLegalDocumentVersionsController);
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var platformAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePlatformPermissionAttribute");
            Assert.True(platformAttr is not null, $"{method.Name} must be protected by RequirePlatformPermission.");
        }
    }

    [Fact]
    public void PublicContentController_AllowsAnonymousOnEveryAction()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Tenant.Legal.LegalDocumentContentController);
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var allowAnonymous = method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false);
            Assert.NotEmpty(allowAnonymous);
        }
    }
}
