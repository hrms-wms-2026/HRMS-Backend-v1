using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.ChecklistTemplates;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>Architecture tests for ChecklistTemplatesController and
/// OnboardingChecklistTemplatesController: controller location, tenant policy, base routes,
/// permission attributes, absence of tenantId exposure, no DELETE endpoint, and strict
/// constructor injection (IMediator only).</summary>
public class ChecklistTemplatesControllerArchitectureTests
{
    private static readonly Type TemplatesControllerType = typeof(ChecklistTemplatesController);
    private static readonly Type MatchesControllerType = typeof(OnboardingChecklistTemplatesController);

    [Fact]
    public void BothControllers_ExistIn_TenantCoreHrNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.CoreHr", TemplatesControllerType.Namespace);
        Assert.Equal("ONEVO.Api.Controllers.Tenant.CoreHr", MatchesControllerType.Namespace);
    }

    [Fact]
    public void BothControllers_RequireTenantPolicy()
    {
        Assert.Equal("TenantPolicy", TemplatesControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("TenantPolicy", MatchesControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void ChecklistTemplatesController_HasCorrectBaseRoute()
    {
        Assert.Equal("api/v1/people/checklist-templates", TemplatesControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void OnboardingChecklistTemplatesController_HasCorrectBaseRoute()
    {
        Assert.Equal("api/v1/onboarding/checklist-templates", MatchesControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void AllFiveChecklistTemplateRoutesAndVerbs_Exist()
    {
        var listMethod = TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.List));
        var getMethod = TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.GetById));
        var createMethod = TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.Create));
        var updateMethod = TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.Update));
        var archiveMethod = TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.Archive));

        Assert.NotNull(listMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(listMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.Equal("{id:guid}", getMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Null(createMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:guid}", updateMethod!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{id:guid}/archive", archiveMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void OnboardingChecklistTemplatesController_MatchesRoute_Exists()
    {
        var matchesMethod = MatchesControllerType.GetMethod(nameof(OnboardingChecklistTemplatesController.Matches));
        Assert.Equal("matches", matchesMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void ReadActions_RequireEmployeesReadPermission()
    {
        Assert.Equal("employees:read", GetPermission(TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.List))!));
        Assert.Equal("employees:read", GetPermission(TemplatesControllerType.GetMethod(nameof(ChecklistTemplatesController.GetById))!));
        Assert.Equal("employees:read", GetPermission(MatchesControllerType.GetMethod(nameof(OnboardingChecklistTemplatesController.Matches))!));
    }

    [Theory]
    [InlineData(nameof(ChecklistTemplatesController.Create))]
    [InlineData(nameof(ChecklistTemplatesController.Update))]
    [InlineData(nameof(ChecklistTemplatesController.Archive))]
    public void MutatingActions_RequireEmployeesWritePermission(string methodName)
    {
        Assert.Equal("employees:write", GetPermission(TemplatesControllerType.GetMethod(methodName)!));
    }

    [Fact]
    public void BothControllers_InjectIMediatorOnly()
    {
        foreach (var controllerType in new[] { TemplatesControllerType, MatchesControllerType })
        {
            var constructors = controllerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Single(constructors);
            var parameters = constructors[0].GetParameters();
            Assert.Single(parameters);
            Assert.Equal("IMediator", parameters[0].ParameterType.Name);
        }
    }

    [Fact]
    public void NoAction_AcceptsTenantIdParameter()
    {
        foreach (var controllerType in new[] { TemplatesControllerType, MatchesControllerType })
        {
            var offenders = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .SelectMany(m => m.GetParameters())
                .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(offenders);
        }
    }

    [Theory]
    [InlineData(typeof(CreateChecklistTemplateRequest))]
    [InlineData(typeof(UpdateChecklistTemplateRequest))]
    public void RequestContracts_DoNotExposeTenantId(Type contractType)
    {
        var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);
        Assert.DoesNotContain(properties, name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoDeleteChecklistTemplateEndpoint_Exists()
    {
        var offenders = TemplatesControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<HttpDeleteAttribute>() is not null);
        Assert.Empty(offenders);
    }

    private static string GetPermission(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
