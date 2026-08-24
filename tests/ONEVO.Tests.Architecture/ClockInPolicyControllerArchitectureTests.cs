using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.ClockInPolicies;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class ClockInPolicyControllerArchitectureTests
{
    private static readonly Type LeControllerType = typeof(LegalEntityClockInPoliciesController);
    private static readonly Type TenantControllerType = typeof(ClockInPoliciesController);

    [Fact]
    public void Controllers_ExistIn_TenantAttendanceNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.Attendance", LeControllerType.Namespace);
        Assert.Equal("ONEVO.Api.Controllers.Tenant.Attendance", TenantControllerType.Namespace);
    }

    [Fact]
    public void Controllers_RequireTenantPolicy()
    {
        Assert.Equal("TenantPolicy", LeControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("TenantPolicy", TenantControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void LegalEntityController_HasExpectedRoute()
    {
        Assert.Equal(
            "api/v1/attendance/legal-entities/{legalEntityId:guid}/clock-in-policies",
            LeControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void TenantController_HasExpectedRoute()
    {
        Assert.Equal(
            "api/v1/attendance/clock-in-policies",
            TenantControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void ReadActions_RequireAttendanceRead()
    {
        AssertPermission(LeControllerType.GetMethod(nameof(LegalEntityClockInPoliciesController.List))!, "attendance:read");
        AssertPermission(LeControllerType.GetMethod(nameof(LegalEntityClockInPoliciesController.Get))!, "attendance:read");
        AssertPermission(TenantControllerType.GetMethod(nameof(ClockInPoliciesController.List))!, "attendance:read");
        AssertPermission(TenantControllerType.GetMethod(nameof(ClockInPoliciesController.Get))!, "attendance:read");
    }

    [Fact]
    public void WriteActions_RequireAttendanceWrite()
    {
        AssertPermission(LeControllerType.GetMethod(nameof(LegalEntityClockInPoliciesController.Create))!, "attendance:write");
        AssertPermission(LeControllerType.GetMethod(nameof(LegalEntityClockInPoliciesController.Update))!, "attendance:write");
        AssertPermission(LeControllerType.GetMethod(nameof(LegalEntityClockInPoliciesController.Archive))!, "attendance:write");
        AssertPermission(LeControllerType.GetMethod(nameof(LegalEntityClockInPoliciesController.Restore))!, "attendance:write");
    }

    [Fact]
    public void NoAction_AcceptsTenantIdParameter()
    {
        foreach (var type in new[] { LeControllerType, TenantControllerType })
        {
            var offenders = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .SelectMany(m => m.GetParameters())
                .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(offenders);
        }
    }

    [Fact]
    public void RequestContract_OmitsTenantId_AndUsesHybridNotEither()
    {
        var props = typeof(UpsertClockInPolicyRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(props, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));

        var workAreaProps = typeof(WorkAreaRulesRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("Hybrid", workAreaProps);
        Assert.DoesNotContain(workAreaProps, n => n.Contains("Either", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Application_DoesNotReference_EfCore()
    {
        var appAsm = typeof(ONEVO.Application.DependencyInjection).Assembly;
        var refs = appAsm.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(refs, n => n is "Microsoft.EntityFrameworkCore" or "Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public void Migration_Declares_Rls_TenantTables()
    {
        var migrationsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ONEVO.Infrastructure", "Migrations"));
        var migration = Directory.GetFiles(migrationsDir, "*AddClockInPolicies.cs")
            .Single(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var source = File.ReadAllText(migration);
        Assert.Contains("TenantTables", source);
        Assert.Contains("\"clock_in_policies\"", source);
        Assert.Contains("\"clock_in_late_deduction_rules\"", source);
        Assert.Contains("tenant_isolation", source);
    }

    private static void AssertPermission(MethodInfo method, string expected)
    {
        var permission = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(permission);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(expected, field!.GetValue(permission));
    }
}
