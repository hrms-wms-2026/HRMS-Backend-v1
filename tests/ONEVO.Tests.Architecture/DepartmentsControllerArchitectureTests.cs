using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.Departments;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture tests for DepartmentsController in Part 2C:
/// Verifies controller location, tenant policy, exact route templates, permission attributes,
/// absence of tenantId and headPositionId in request parameters/contracts, and strict constructor injection (IMediator only).
/// </summary>
public class DepartmentsControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(DepartmentsController);

    [Fact]
    public void Controller_ExistsIn_TenantOrgStructureNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.OrgStructure", ControllerType.Namespace);
    }

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var authorizeAttribute = ControllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal("TenantPolicy", authorizeAttribute!.Policy);
    }

    [Fact]
    public void Controller_HasCorrectBaseRoute_WithLegalEntityIdGuidConstraint()
    {
        var routeAttr = ControllerType.GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("api/v1/org/legal-entities/{legalEntityId:guid}/departments", routeAttr!.Template);
    }

    [Fact]
    public void NoRoute_UsesUnscopedDepartmentsPath()
    {
        var routeAttr = ControllerType.GetCustomAttribute<RouteAttribute>();
        Assert.False(string.Equals(routeAttr?.Template, "api/v1/org/departments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AllFiveRequiredRoutesAndVerbs_Exist()
    {
        var listMethod = ControllerType.GetMethod(nameof(DepartmentsController.List));
        var getMethod = ControllerType.GetMethod(nameof(DepartmentsController.Get));
        var createMethod = ControllerType.GetMethod(nameof(DepartmentsController.Create));
        var updateMethod = ControllerType.GetMethod(nameof(DepartmentsController.Update));
        var deleteMethod = ControllerType.GetMethod(nameof(DepartmentsController.Delete));

        Assert.NotNull(listMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(listMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(getMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal("{departmentId:guid}", getMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(createMethod?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(createMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.NotNull(updateMethod?.GetCustomAttribute<HttpPutAttribute>());
        Assert.Equal("{departmentId:guid}", updateMethod!.GetCustomAttribute<HttpPutAttribute>()!.Template);

        Assert.NotNull(deleteMethod?.GetCustomAttribute<HttpDeleteAttribute>());
        Assert.Equal("{departmentId:guid}", deleteMethod!.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    private static IEnumerable<MethodInfo> ActionMethods()
        => ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    [Fact]
    public void GetActions_RequireOrgReadPermission()
    {
        var listMethod = ControllerType.GetMethod(nameof(DepartmentsController.List));
        var getMethod = ControllerType.GetMethod(nameof(DepartmentsController.Get));

        Assert.Equal("org:read", GetPermission(listMethod!));
        Assert.Equal("org:read", GetPermission(getMethod!));
    }

    [Theory]
    [InlineData(nameof(DepartmentsController.Create))]
    [InlineData(nameof(DepartmentsController.Update))]
    [InlineData(nameof(DepartmentsController.Delete))]
    public void MutatingActions_RequireOrgManagePermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        Assert.Equal("org:manage", GetPermission(method!));
    }

    [Fact]
    public void ArchiveRoute_ExistsAsPost_WithOrgManagePermission()
    {
        var archiveMethod = ControllerType.GetMethod(nameof(DepartmentsController.Archive));

        Assert.NotNull(archiveMethod);
        Assert.NotNull(archiveMethod!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{departmentId:guid}/archive", archiveMethod.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("org:manage", GetPermission(archiveMethod));
    }

    [Fact]
    public void ArchiveCheckRoute_ExistsAsPost_WithOrgReadPermission()
    {
        var method = ControllerType.GetMethod(nameof(DepartmentsController.ArchiveCheck));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{departmentId:guid}/archive-check", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("org:read", GetPermission(method));
    }

    [Fact]
    public void RestoreRoute_ExistsAsPost_WithOrgManagePermission()
    {
        var method = ControllerType.GetMethod(nameof(DepartmentsController.Restore));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{departmentId:guid}/restore", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("org:manage", GetPermission(method));
    }

    [Fact]
    public void DeleteDepartmentCommand_TypeNoLongerExists_ArchiveWordingUsedInstead()
    {
        var assembly = typeof(ArchiveDepartmentCommand).Assembly;
        var offender = assembly.GetType("ONEVO.Application.Features.OrgStructure.Commands.DeleteDepartment.DeleteDepartmentCommand");

        Assert.Null(offender);
    }

    [Fact]
    public void NoAction_AcceptsTenantIdParameter()
    {
        var offenders = ActionMethods()
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAction_AcceptsHeadPositionIdParameter()
    {
        var offenders = ActionMethods()
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "headPositionId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    // RequestContracts_DoNotExposeTenantId_LegalEntityId_OrHeadPositionId was narrowed here: it
    // asserted the request contracts had no HeadPositionId property, which was correct for
    // Part 2C scope but is now obsolete now that Part 3 legitimately adds Guid? HeadPositionId
    // to both request contracts (legal entity scope still comes only from the route - see
    // DepartmentPart3ArchitectureTests.cs for the HeadPositionId-specific guards).
    [Theory]
    [InlineData(typeof(CreateDepartmentRequest))]
    [InlineData(typeof(UpdateDepartmentRequest))]
    public void RequestContracts_DoNotExposeTenantId_OrLegalEntityId(Type contractType)
    {
        var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(properties, name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => string.Equals(name, "LegalEntityId", StringComparison.OrdinalIgnoreCase));
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
    public void Controller_DoesNotInjectDbContext_Repositories_OrUserServicesDirectly()
    {
        var fields = ControllerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var fieldTypeNames = fields.Select(f => f.FieldType.Name).ToList();

        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("DbContext", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("Repository", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HeadPositionIdSchema_Part2AGuardStillPasses()
    {
        var deptType = typeof(ONEVO.Domain.Features.OrgStructure.Entities.Department);
        var property = deptType.GetProperty("HeadPositionId");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid?), property.PropertyType);
    }

    private static string GetPermission(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
