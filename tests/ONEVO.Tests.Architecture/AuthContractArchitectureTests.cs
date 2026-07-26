using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;

namespace ONEVO.Tests.Architecture;

public sealed class AuthContractArchitectureTests
{
    [Fact]
    public void AuthControllers_DeclareNoNestedRequestOrResponseTypes()
    {
        var apiAssembly = typeof(AuthLoginController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t.Namespace == "ONEVO.Api.Controllers.Tenant.Auth" && t.IsClass);

        foreach (var controllerType in controllerTypes)
        {
            controllerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(t => t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
                .Should()
                .BeEmpty($"{controllerType.Name} must not declare nested request/response types");
        }
    }

    [Fact]
    public void AcceptInvitationPasswordRequest_ContainsOnlyProductApprovedFields()
    {
        typeof(AcceptInvitationPasswordRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo("Password", "ConfirmPassword", "Acceptances");
    }

    [Fact]
    public void PendingLegalCompletion_UsesOnlyCanonicalRoute()
    {
        var source = ReadSource(
            "src",
            "ONEVO.Api",
            "Controllers",
            "Tenant",
            "Auth",
            "AuthPendingLegalController.cs");

        source.Should().Contain("/api/v1/legal/acceptances/complete-login");
        source.Should().NotContain("legal-pending/accept");
    }

    [Fact]
    public void LoginAndInvitationHandlers_DoNotUsePolicyFlagsAsMethodGates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "src", "ONEVO.Application", "Features", "Auth", "Login"),
            Path.Combine(repositoryRoot, "src", "ONEVO.Application", "Features", "Auth", "Invite")
        };
        var forbidden = new[]
        {
            "password_login_enabled",
            "google_login_enabled",
            "PasswordCompletionAllowed",
            "GoogleCompletionAllowed"
        };

        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories)))
        {
            var source = File.ReadAllText(file);
            foreach (var term in forbidden)
                source.Should().NotContain(term, $"{Path.GetFileName(file)} must not gate a login method with {term}");
        }
    }

    [Fact]
    public void CurrentUserInternalIdentifiers_AreNeverSerialized()
    {
        var properties = typeof(ONEVO.Application.Features.Auth.Login.DTOs.Responses.CurrentUserDto)
            .GetProperties()
            .ToDictionary(p => p.Name);

        properties["UserId"].GetCustomAttribute<JsonIgnoreAttribute>().Should().NotBeNull();
        properties["TenantId"].GetCustomAttribute<JsonIgnoreAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void PreTenantChallengeLookups_UseNarrowAdminModeAndCloseTheConnection()
    {
        var legalRepository = ReadSource(
            "src",
            "ONEVO.Infrastructure",
            "Persistence",
            "Repositories",
            "Auth",
            "Legal",
            "EfLegalLoginChallengeRepository.cs");
        var mfaStore = ReadSource(
            "src",
            "ONEVO.Infrastructure",
            "Identity",
            "Mfa",
            "PostgresMfaChallengeStore.cs");

        foreach (var source in new[] { legalRepository, mfaStore })
        {
            source.Should().Contain("ForPreTenantContinuationAsync");
            source.Should().Contain("_tenantContext.SetAdminMode()");
            source.Should().Contain("CloseConnectionIfOpenAsync()");
            source.Should().Contain("_tenantContext.SetSystemMode()");
            source.Should().NotContain("IgnoreQueryFilters");
        }
    }

    [Fact]
    public void ContinuationHandlers_SwitchToChallengeTenantBeforeUserReads()
    {
        var legalHandler = ReadSource(
            "src",
            "ONEVO.Application",
            "Features",
            "Auth",
            "Legal",
            "Commands",
            "AcceptPendingLegalDocuments",
            "AcceptPendingLegalDocumentsCommandHandler.cs");
        var mfaHandler = ReadSource(
            "src",
            "ONEVO.Application",
            "Features",
            "Auth",
            "Login",
            "Commands",
            "MfaVerify",
            "VerifyMfaCommandHandler.cs");

        legalHandler.IndexOf("SwitchToTenantAsync", StringComparison.Ordinal)
            .Should().BeLessThan(legalHandler.IndexOf("_users.GetByIdAsync", StringComparison.Ordinal));
        mfaHandler.IndexOf("SwitchToTenantAsync", StringComparison.Ordinal)
            .Should().BeLessThan(mfaHandler.IndexOf("_users.GetByIdAsync", StringComparison.Ordinal));
        mfaHandler.Should().Contain("GetForPreTenantContinuationAsync");
        mfaHandler.Should().NotContain(
            "if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)");
    }

    [Fact]
    public void RootLoginContinuation_UsesOriginalRequestHost()
    {
        var controller = ReadSource(
            "src",
            "ONEVO.Api",
            "Controllers",
            "Tenant",
            "Auth",
            "TenantAuthResponseWriter.cs");

        controller.Should().Contain(
            "ContinueUrl = $\"{controller.Request.Scheme}://{controller.Request.Host.Value}{path}\"");
        controller.Should().NotContain(
            "ContinueUrl = $\"{controller.Request.Scheme}://{_tenantContext.Slug}");
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
