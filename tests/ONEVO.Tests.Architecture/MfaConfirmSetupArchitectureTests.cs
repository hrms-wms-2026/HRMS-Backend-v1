using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Architecture;

public sealed class MfaConfirmSetupArchitectureTests
{
    [Fact]
    public void ConfirmMfaSetupRequest_ContainsOnlyCode()
    {
        typeof(ConfirmMfaSetupRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo("Code");
    }

    [Fact]
    public void NoMfaContract_AcceptsTenantIdOrUserId()
    {
        var apiAssembly = typeof(AuthMfaController).Assembly;
        var mfaContracts = new[]
        {
            typeof(ConfirmMfaSetupRequest),
            typeof(VerifyMfaRequest)
        };

        foreach (var contract in mfaContracts)
        {
            var propertyNames = contract.GetProperties().Select(p => p.Name);
            propertyNames.Should().NotContain(
                name => name.Equals("TenantId", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("UserId", StringComparison.OrdinalIgnoreCase),
                $"{contract.Name} must not accept tenant_id or user_id from the request body");
        }

        _ = apiAssembly;
    }

    [Fact]
    public void ConfirmMfaSetupRoute_RequiresTenantPolicy()
    {
        var method = typeof(AuthMfaController).GetMethod(
            nameof(AuthMfaController.ConfirmMfaSetup), BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("mfa/confirm-setup");

        var authorizeAttribute = method.GetCustomAttribute<AuthorizeAttribute>();
        authorizeAttribute.Should().NotBeNull("confirm-setup must require authentication");
        authorizeAttribute!.Policy.Should().Be("TenantPolicy");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull(
            "confirm-setup must not be reachable anonymously");
    }

    [Fact]
    public void VerifyMfaRoute_RemainsAllowAnonymous()
    {
        var method = typeof(AuthMfaController).GetMethod(
            nameof(AuthMfaController.VerifyMfa), BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull(
            "login mfa/verify must remain AllowAnonymous");
        method.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull(
            "login mfa/verify must not gain a TenantPolicy requirement");
    }

    [Fact]
    public void EnableMfaRoute_StillRequiresTenantPolicy()
    {
        var method = typeof(AuthMfaController).GetMethod(
            nameof(AuthMfaController.EnableMfa), BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        var authorizeAttribute = method!.GetCustomAttribute<AuthorizeAttribute>();
        authorizeAttribute.Should().NotBeNull();
        authorizeAttribute!.Policy.Should().Be("TenantPolicy");
    }

    [Fact]
    public void MfaSetupDto_NeverExposesAnEncryptedSecretField()
    {
        typeof(MfaSetupDto)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .NotContain(name => name.Contains("Encrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MfaFeatureCode_NeverReferencesSystemConfigOrServiceKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mfaSourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "MfaEnable"),
            Path.Combine(repositoryRoot, "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "MfaVerify"),
            Path.Combine(repositoryRoot, "src", "ONEVO.Application", "Features", "Auth", "Login", "Commands", "MfaConfirmSetup")
        };
        var forbidden = new[] { "SystemConfig", "PlatformServiceKey", "PlatformOAuthApp", "IntegrationCredential" };

        foreach (var root in mfaSourceRoots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                foreach (var term in forbidden)
                    source.Should().NotContain(term, $"{Path.GetFileName(file)} must not reference {term}");
            }
        }
    }

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
