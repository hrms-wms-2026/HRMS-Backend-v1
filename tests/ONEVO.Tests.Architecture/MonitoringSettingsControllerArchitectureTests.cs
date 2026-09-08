using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Monitoring.Settings;
using ONEVO.Api.Controllers.Tenant.Monitoring.Settings;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Policy.DTOs;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Reflection-based coverage for the monitoring policy configuration surface added to
/// MonitoringSettingsController: the four company/department/position/role override
/// routes must carry the correct [RequirePermission] value, and none of the request
/// contracts may accept a tenantId (tenant identity must come only from ICurrentUser).
/// </summary>
public sealed class MonitoringSettingsControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(MonitoringSettingsController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        Assert.Equal("TenantPolicy", ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void GetAttendanceMonitoringPolicy_HasExpectedRouteAndPermission()
    {
        var method = ControllerType.GetMethod(nameof(MonitoringSettingsController.GetAttendanceMonitoringPolicy))!;
        AssertPermission(method, "monitoring:read");
        AssertHttpGetTemplate(method, "~/api/v1/attendance/monitoring/policy");
    }

    [Fact]
    public void UpdateAttendanceMonitoringCompany_HasExpectedRouteAndPermission()
    {
        var method = ControllerType.GetMethod(nameof(MonitoringSettingsController.UpdateAttendanceMonitoringCompany))!;
        AssertPermission(method, "monitoring:configure");
        AssertHttpPutTemplate(method, "~/api/v1/attendance/monitoring/policy/company");
    }

    [Fact]
    public void UpsertAttendanceMonitoringOverride_HasExpectedRouteAndPermission()
    {
        var method = ControllerType.GetMethod(nameof(MonitoringSettingsController.UpsertAttendanceMonitoringOverride))!;
        AssertPermission(method, "monitoring:configure");
        AssertHttpPutTemplate(method, "~/api/v1/attendance/monitoring/policy/{scopeType}/{targetId:guid}");
    }

    [Fact]
    public void DeleteAttendanceMonitoringOverride_HasExpectedRouteAndPermission()
    {
        var method = ControllerType.GetMethod(nameof(MonitoringSettingsController.DeleteAttendanceMonitoringOverride))!;
        AssertPermission(method, "monitoring:configure");

        var route = method.GetCustomAttribute<HttpDeleteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("~/api/v1/attendance/monitoring/policy/{scopeType}/{targetId:guid}", route!.Template);
    }

    /// <summary>
    /// TrayAgentPolicyDto is served by the tray policy endpoint
    /// (GET /api/v1/monitoring/tray/policy). Proves the wire contract for the newly-added
    /// LocationTrackingEnabled/EffectiveScope fields carries the expected snake_case JSON names,
    /// independent of the end-to-end HTTP test (which is currently blocked by an unrelated
    /// pre-existing 500 in the tray activation/exchange flow - see integration test notes).
    /// </summary>
    [Fact]
    public void TrayAgentPolicyDto_NewFields_HaveExpectedJsonPropertyNames()
    {
        var effectiveScopeProperty = typeof(TrayAgentPolicyDto).GetProperty(nameof(TrayAgentPolicyDto.EffectiveScope))!;
        var locationProperty = typeof(TrayAgentPolicyDto).GetProperty(nameof(TrayAgentPolicyDto.LocationTrackingEnabled))!;

        Assert.Equal(
            "effective_scope",
            effectiveScopeProperty.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name);
        Assert.Equal(
            "location_tracking_enabled",
            locationProperty.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name);
    }

    [Fact]
    public void RequestContracts_DoNotAcceptTenantIdOrLegalEntityId()
    {
        foreach (var type in new[]
                 {
                     typeof(UpdateMonitoringFeatureTogglesRequest),
                     typeof(UpsertMonitoringPolicyOverrideRequest)
                 })
        {
            var props = type.GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain(props, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(props, n => string.Equals(n, "LegalEntityId", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(props, n => string.Equals(n, "ActorId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void NoAction_AcceptsTenantIdOrActorIdParameter()
    {
        var offenders = ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Name, "actorId", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(offenders);
    }

    private static void AssertPermission(MethodInfo method, string expected)
    {
        var permission = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(permission);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(expected, field!.GetValue(permission));
    }

    private static void AssertHttpGetTemplate(MethodInfo method, string expectedTemplate)
    {
        var route = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(route);
        Assert.Equal(expectedTemplate, route!.Template);
    }

    private static void AssertHttpPutTemplate(MethodInfo method, string expectedTemplate)
    {
        var route = method.GetCustomAttribute<HttpPutAttribute>();
        Assert.NotNull(route);
        Assert.Equal(expectedTemplate, route!.Template);
    }
}
