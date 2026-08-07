using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the Part 2C controller wiring for Legal Entity / Company General
/// Settings: correct folder ownership, correct permission on every action,
/// no TenantId anywhere in the HTTP surface, and no PUT /logo route (Part 2C
/// deliberately deferred it - see the Part 2C report §5).
/// </summary>
public class LegalEntitiesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LegalEntitiesController);

    [Fact]
    public void Controller_LivesUnderTenantOrgStructure_NotAdminOrDevPlatform()
    {
        var ns = ControllerType.Namespace ?? string.Empty;

        Assert.Contains("Tenant", ns);
        Assert.Contains("OrgStructure", ns);
        Assert.DoesNotContain("Admin", ns);
        Assert.DoesNotContain("DevPlatform", ns);
    }

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var authorizeAttribute = ControllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal("TenantPolicy", authorizeAttribute!.Policy);
    }

    private static IEnumerable<MethodInfo> ActionMethods()
        => ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    [Fact]
    public void EveryAction_HasARequirePermissionAttribute()
    {
        var missing = ActionMethods()
            .Where(m => m.GetCustomAttribute<RequirePermissionAttribute>() is null)
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void ListAction_UsesOrgRead()
    {
        var listMethod = ControllerType.GetMethod(nameof(LegalEntitiesController.List));
        var permission = GetPermission(listMethod!);

        Assert.Equal("org:read", permission);
    }

    [Fact]
    public void GetGeneralSettingsAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.GetGeneralSettings));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void CreateAction_UsesLegalEntityCreate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.Create));
        GetPermission(method!).Should().Be("legal_entity:create");
    }

    [Fact]
    public void UpdateGeneralSettingsAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.UpdateGeneralSettings));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void DeleteAction_UsesLegalEntityDelete()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.Delete));
        GetPermission(method!).Should().Be("legal_entity:delete");
    }

    [Fact]
    public void RemoveLogoAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.RemoveLogo));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void GetLogoAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.GetLogo));
        GetPermission(method!).Should().Be("legal_entity:update");
    }

    [Fact]
    public void NoAction_UsesOrgManage()
    {
        var offenders = ActionMethods()
            .Select(GetPermission)
            .Where(p => p == "org:manage")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAction_AcceptsUserIdParameter()
    {
        var offenders = ActionMethods()
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "userId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(typeof(ONEVO.Api.Contracts.OrgStructure.LegalEntities.CreateLegalEntityRequest))]
    [InlineData(typeof(ONEVO.Api.Contracts.OrgStructure.LegalEntities.UpdateLegalEntityGeneralSettingsRequest))]
    [InlineData(typeof(ONEVO.Api.Contracts.OrgStructure.LegalEntities.DeleteLegalEntityRequest))]
    public void RequestContracts_DoNotAcceptTenantIdOrUserId(Type requestType)
    {
        var offenders = requestType.GetProperties()
            .Where(p =>
                string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, "userId", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAction_UsesSettingsAdminPermission()
    {
        var offenders = ActionMethods()
            .Select(GetPermission)
            .Where(p => p == "settings:admin")
            .ToList();

        Assert.Empty(offenders);
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
    public void NoPutLogoRoute_Exists()
    {
        var offenders = ActionMethods()
            .Select(m => m.GetCustomAttribute<HttpPutAttribute>())
            .Where(a => a?.Template?.Contains("logo", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DeleteLogoRoute_Exists_AndUsesDeleteVerb()
    {
        var removeLogo = ControllerType.GetMethod(nameof(LegalEntitiesController.RemoveLogo));
        var httpDelete = removeLogo!.GetCustomAttribute<HttpDeleteAttribute>();

        Assert.NotNull(httpDelete);
        Assert.Contains("logo", httpDelete!.Template ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_DoesNotReferenceFileRecordRepositoryDirectly()
    {
        // Redundant with the pre-existing FileStorageArchitectureTests guard,
        // but pins the specific expectation for this controller: it must never
        // gain a direct file-repository dependency now that PUT /logo is deferred.
        var fields = ControllerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var offenders = fields
            .Where(f => f.FieldType.FullName?.Contains("FileRecordRepository") == true)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string GetPermission(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
