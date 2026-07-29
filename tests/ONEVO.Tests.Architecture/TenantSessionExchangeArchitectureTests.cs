using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the base-domain-to-tenant-host session exchange handoff (see
/// TENANT_SESSION_EXCHANGE_LOGIN_FLOW_REPORT.md): the exchange code is never stored or exposed in
/// plaintext, no browser-facing DTO leaks tenant_id/user_id, the base host never signs in before a
/// gate clears, /me never creates a session, and no shared parent-domain session cookie exists.
/// </summary>
public sealed class TenantSessionExchangeArchitectureTests
{
    [Fact]
    public void TenantSessionExchangeChallenge_HasNoRawOrPlaintextCodeProperty()
    {
        var properties = typeof(TenantSessionExchangeChallenge).GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain("RawCode");
        properties.Should().NotContain("Code");
        properties.Should().NotContain("CodePlaintext");
        properties.Should().OnlyContain(
            name => !name.Contains("Raw", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Plain", StringComparison.OrdinalIgnoreCase),
            "the entity must persist only CodeHash, never the raw/plaintext opaque code");
        properties.Should().Contain("CodeHash");
    }

    [Fact]
    public void TenantSessionExchangeResponseDto_ExposesNoTenantIdUserIdOrTokenMaterial()
    {
        var guidProperties = typeof(TenantSessionExchangeResponseDto)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));
        guidProperties.Should().BeEmpty("the exchange-required response must never expose a Guid-typed id");

        var forbiddenSubstrings = new[] { "token", "jwt", "session", "permission", "password" };
        var propertyNames = typeof(TenantSessionExchangeResponseDto).GetProperties().Select(p => p.Name).ToList();
        foreach (var name in propertyNames)
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                name.Contains(forbidden, StringComparison.OrdinalIgnoreCase).Should().BeFalse(
                    $"TenantSessionExchangeResponseDto.{name} must not resemble {forbidden} material");
            }
        }

        typeof(TenantSessionExchangeUserDto).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Email"], "the exchange user projection must carry only email, never an id");
    }

    [Fact]
    public void TenantSessionExchangeRequest_AcceptsOnlyCode_NeverTenantOrUserIdentifiers()
    {
        typeof(TenantSessionExchangeRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo(["Code"], "the tenant is resolved solely from the request host, never from the body");
    }

    [Fact]
    public void SessionExchangeEndpoint_IsAllowAnonymous_AndTenantHostGated()
    {
        var method = typeof(AuthSessionController).GetMethod("SessionExchange");
        method.Should().NotBeNull();
        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull(
            "the tenant host has no session yet when this is called");
        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("session-exchange");

        var source = ReadSource("src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "AuthSessionController.cs");
        source.Should().Contain("_tenantContext.ContextMode != TenantContextMode.Tenant");
        source.Should().Contain("statusCode: 400");
    }

    [Fact]
    public void HandleSessionResultAsync_ChecksTenantSessionExchangeBeforeSigningIn()
    {
        var source = ReadSource("src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "TenantAuthResponseWriter.cs");

        var exchangeCheckIndex = source.IndexOf("dto.RequiresTenantSessionExchange", StringComparison.Ordinal);
        var signInIndex = source.IndexOf("await controller.SignInAsync(dto, env);", StringComparison.Ordinal);

        exchangeCheckIndex.Should().BeGreaterThan(-1);
        signInIndex.Should().BeGreaterThan(-1);
        exchangeCheckIndex.Should().BeLessThan(signInIndex,
            "a base-domain login that still needs tenant-host exchange must never reach the direct sign-in branch");
    }

    [Fact]
    public void GetCurrentSessionQueryHandler_NeverCreatesOrSignsInASession()
    {
        var source = ReadSource(
            "src", "ONEVO.Application", "Features", "Auth", "Login", "Queries", "GetCurrentSession",
            "GetCurrentSessionQueryHandler.cs");

        source.Should().NotContain("SignInAsync");
        source.Should().NotContain("TenantDatabaseTicketStore");
        source.Should().NotContain("ITenantSessionExchangeService");
        source.Should().NotContain("ILoginSessionMaterialFactory");
        source.Should().Contain("Authentication required.", "/me must 401 rather than establish a session when unauthenticated");
    }

    [Fact]
    public void TenantSessionCookie_IsNeverDomainScoped()
    {
        var authExtensions = ReadSource("src", "ONEVO.Api", "Extensions", "AuthenticationExtensions.cs");
        var responseWriter = ReadSource("src", "ONEVO.Api", "Controllers", "Tenant", "Auth", "TenantAuthResponseWriter.cs");

        authExtensions.Should().NotContain("Cookie.Domain",
            "onevo_session must stay host-scoped - no shared parent-domain cookie");
        responseWriter.Should().NotContain("Domain =",
            "no cookie written by the tenant controllers may carry a shared parent-domain Domain attribute");
    }

    [Fact]
    public void TenantSessionExchangeMigration_EnablesRowLevelSecurityWithTenantIsolationPolicy()
    {
        var migrationsDirectory = Path.Combine(FindRepositoryRoot(), "src", "ONEVO.Infrastructure", "Migrations");
        var migrationFile = Directory.EnumerateFiles(migrationsDirectory, "*_AddTenantSessionExchangeChallenges.cs")
            .SingleOrDefault();

        migrationFile.Should().NotBeNull("the session exchange migration must exist");
        var source = File.ReadAllText(migrationFile!);

        source.Should().Contain("ENABLE ROW LEVEL SECURITY");
        source.Should().Contain("FORCE ROW LEVEL SECURITY");
        source.Should().Contain("CREATE POLICY tenant_isolation");
    }

    [Fact]
    public void TenantSessionExchangeChallengeRepository_ConsumesWithSingleGuardedUpdate_NotReadThenWrite()
    {
        var source = ReadSource(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "Auth", "Login",
            "EfTenantSessionExchangeChallengeRepository.cs");

        source.Should().Contain("ExecuteUpdateAsync");
        // The guarded WHERE clause (ConsumedAt == null, ExpiresAt > now) must sit on the same
        // ExecuteUpdateAsync call, not a separate prior read used to decide whether to write.
        var updateCallIndex = source.IndexOf("ExecuteUpdateAsync(", StringComparison.Ordinal);
        var consumedAtFilterIndex = source.IndexOf("c.ConsumedAt == null", StringComparison.Ordinal);
        consumedAtFilterIndex.Should().BeGreaterThan(-1);
        consumedAtFilterIndex.Should().BeLessThan(updateCallIndex);
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
