using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the workspace object added to every tenant-resolved auth response
/// (see LOGIN_WORKSPACE_RESPONSE_FIX_REPORT.md): it must expose only slug/display_name, never a
/// tenant Guid, and none of the internal-only secrets LoginResponseDto carries between the handler
/// and the controller may leak into the DTO actually serialized to the browser.
/// </summary>
public sealed class WorkspaceResponseArchitectureTests
{
    [Fact]
    public void WorkspaceResponseDto_ExposesOnlySlugAndDisplayName()
    {
        typeof(WorkspaceResponseDto)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo("Slug", "DisplayName");
    }

    [Fact]
    public void WorkspaceResponseDto_HasNoTenantIdOrOtherGuidProperty()
    {
        typeof(WorkspaceResponseDto)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?))
            .Should()
            .BeEmpty("workspace is a public, browser-facing DTO and must never carry the tenant's internal id");
    }

    [Fact]
    public void WorkspaceResponseDto_JsonPropertyNames_MatchProductContract()
    {
        var properties = typeof(WorkspaceResponseDto).GetProperties().ToDictionary(p => p.Name);

        properties["Slug"].GetCustomAttribute<JsonPropertyNameAttribute>()!.Name.Should().Be("slug");
        properties["DisplayName"].GetCustomAttribute<JsonPropertyNameAttribute>()!.Name.Should().Be("display_name");
    }

    [Fact]
    public void AuthSessionResponseDto_HasNoUnignoredGuidProperty()
    {
        // Every other Guid-shaped value on the browser-facing session DTO (workspace's tenant id,
        // user id, etc.) must be either absent entirely or [JsonIgnore]d - CurrentUserDto.UserId/
        // TenantId are the one legacy exception, already covered by
        // AuthContractArchitectureTests.CurrentUserInternalIdentifiers_AreNeverSerialized.
        var guidProperties = typeof(AuthSessionResponseDto)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));

        guidProperties.Should().BeEmpty(
            "AuthSessionResponseDto must never directly expose a Guid-typed property to the browser");
    }

    [Fact]
    public void AuthSessionResponseDto_DoesNotExposeAnyInternalSecretFields()
    {
        // LoginResponseDto (the internal handler-to-controller result) carries CsrfToken/
        // CsrfTokenHash/MfaChallenge/LegalChallenge/LegalCsrfToken - none of these may exist on the
        // DTO that actually gets serialized to the browser.
        var forbiddenNames = new[]
        {
            "CsrfToken", "CsrfTokenHash", "MfaChallenge", "LegalChallenge", "LegalCsrfToken",
            "PasswordHash", "SessionKey"
        };

        var actualNames = typeof(AuthSessionResponseDto).GetProperties().Select(p => p.Name).ToList();

        foreach (var forbidden in forbiddenNames)
            actualNames.Should().NotContain(forbidden, $"AuthSessionResponseDto must never expose {forbidden}");
    }

    [Fact]
    public void LoginResponseDto_DocCommentStillDeclaresInternalOnlySecrets()
    {
        // Guards the existing internal-only contract documented on LoginResponseDto - if this
        // comment is ever removed, someone may have started serializing the handler DTO directly
        // instead of routing it through ToSessionResponse().
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "DTOs", "Responses", "LoginResponseDto.cs");

        source.Should().Contain("none of the four are ever serialized to the");
        source.Should().Contain("client. Only ToSessionResponse()/ToTenantSessionExchangeResponse() are ever returned as JSON");
    }

    [Fact]
    public void TenantHostPasswordLogin_RemainsRejected()
    {
        var source = ReadSource("src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthLoginController.cs");

        source.Should().Contain("Tenant-host password login is not supported.");
        source.Should().Contain("_tenantContext.ContextMode == TenantContextMode.Tenant");
    }

    [Fact]
    public void LoginContinuationService_PopulatesWorkspaceOnEveryContinuationResponse()
    {
        // Confirms the shared chokepoint used by every login entry point (BaseLogin, BaseGoogleLogin,
        // SelectWorkspace, MfaVerify, AcceptPendingLegalDocuments, ForcePasswordChange, invitation
        // acceptance) builds a Tenant-backed Workspace for every LoginResponseDto branch it returns.
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Services", "LoginContinuationService.cs");

        source.Should().Contain("LoginMapper.ToPasswordChangeRequired(user, tenant)");
        source.Should().Contain("LoginMapper.ToMfaRequired(user, tenant, mfaChallenge)");
        source.Should().Contain("LoginMapper.ToLegalAcceptanceRequired(");
        source.Should().Contain("user, resolvedTenant, rawChallenge, rawCsrf, legalCheck.PendingDocuments");
        source.Should().Contain("_tenantSessionExchange.CreateAsync(");
    }

    [Fact]
    public void GetCurrentSessionQueryHandler_PopulatesWorkspaceFromTenantContextAndRepository()
    {
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Queries", "GetCurrentSession",
            "GetCurrentSessionQueryHandler.cs");

        source.Should().Contain("ITenantRepository");
        source.Should().Contain("new WorkspaceResponseDto(_tenantContext.Slug!, tenant.Name)");
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
