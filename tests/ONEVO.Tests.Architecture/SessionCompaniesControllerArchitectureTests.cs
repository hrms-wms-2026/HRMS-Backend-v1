using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class SessionCompaniesControllerArchitectureTests
{
    [Fact]
    public void SessionCompanies_RequiresTenantPolicy_ButNoOrgPermission()
    {
        var controllerType = typeof(SessionController);
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var method = controllerType.GetMethod(nameof(SessionController.ListCompanies));

        Assert.Equal("TenantPolicy", authorize?.Policy);
        Assert.NotNull(method);
        Assert.Empty(method!.GetCustomAttributes<RequirePermissionAttribute>());
    }

    [Fact]
    public void OrganizationStructureEndpoints_KeepOrgRead()
    {
        Assert.Equal("org:read", PermissionOn<ONEVO.Api.Controllers.Tenant.OrgStructure.DepartmentsController>("List"));
        Assert.Equal("org:read", PermissionOn<ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController>("List"));
    }

    private static string PermissionOn<TController>(string methodName)
    {
        var method = typeof(TController).GetMethod(methodName);
        var attribute = method?.GetCustomAttribute<RequirePermissionAttribute>();
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(attribute);
        Assert.NotNull(field);
        return (string)field!.GetValue(attribute!)!;
    }
}
