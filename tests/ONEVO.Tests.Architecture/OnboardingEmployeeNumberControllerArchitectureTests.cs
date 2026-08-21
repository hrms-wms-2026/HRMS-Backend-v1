using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Onboarding;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class OnboardingEmployeeNumberControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(OnboardingEmployeeNumberController);

    [Fact]
    public void Controller_ExistsIn_TenantCoreHrNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.CoreHr", ControllerType.Namespace);
    }

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        Assert.Equal("TenantPolicy", ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void Controller_HasOnboardingBaseRoute()
    {
        Assert.Equal("api/v1/onboarding", ControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void Suggest_RequiresEmployeesWrite_AndUsesSuggestionRoute()
    {
        var method = ControllerType.GetMethod(nameof(OnboardingEmployeeNumberController.Suggest));
        Assert.Equal("employee-number-suggestion", method!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        var permission = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(permission);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", field!.GetValue(permission));
    }

    [Fact]
    public void Availability_RequiresEmployeesWrite_AndUsesAvailabilityRoute()
    {
        var method = ControllerType.GetMethod(nameof(OnboardingEmployeeNumberController.Availability));
        Assert.Equal("employee-number-availability", method!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        var permission = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(permission);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", field!.GetValue(permission));
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructors = ControllerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(constructors);
        var parameters = constructors[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal("IMediator", parameters[0].ParameterType.Name);
    }

    [Fact]
    public void NoAction_AcceptsTenantIdParameter()
    {
        var offenders = ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(offenders);
    }

    [Fact]
    public void ViewModels_OmitTenantId()
    {
        Assert.DoesNotContain(
            typeof(EmployeeNumberSuggestionViewModel).GetProperties().Select(p => p.Name),
            n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(EmployeeNumberAvailabilityViewModel).GetProperties().Select(p => p.Name),
            n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UniqueIndex_IsTenantPlusEmployeeNumber()
    {
        var configPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations", "CoreHr", "Employee",
            "EmployeeConfiguration.cs"));
        Assert.True(File.Exists(configPath), $"Missing configuration at {configPath}");
        var source = File.ReadAllText(configPath);
        Assert.Contains("HasIndex(e => new { e.TenantId, e.EmployeeNumber }).IsUnique()", source);
    }
}
