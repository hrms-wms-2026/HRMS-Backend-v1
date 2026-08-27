using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.Corrections;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Api.Filters;

namespace ONEVO.Tests.Architecture;

public sealed class AttendanceCorrectionsArchitectureTests
{
    private static readonly Type ControllerType = typeof(AttendanceCorrectionsController);

    [Fact]
    public void Controller_IsTenantScoped_AndUsesExpectedRoute()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.Attendance", ControllerType.Namespace);
        Assert.Equal("TenantPolicy", ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("api/v1/attendance/corrections", ControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void ReviewActions_RequireAttendanceApproval()
    {
        AssertPermission(nameof(AttendanceCorrectionsController.Approvals), "attendance:approve");
        AssertPermission(nameof(AttendanceCorrectionsController.Approve), "attendance:approve");
        AssertPermission(nameof(AttendanceCorrectionsController.Reject), "attendance:approve");
    }

    [Fact]
    public void SelfServiceActions_DoNotRequireApprovalPermission()
    {
        foreach (var name in new[]
        {
            nameof(AttendanceCorrectionsController.Preview),
            nameof(AttendanceCorrectionsController.RequestCorrection),
            nameof(AttendanceCorrectionsController.My),
            nameof(AttendanceCorrectionsController.Cancel)
        })
        {
            Assert.Null(ControllerType.GetMethod(name)!.GetCustomAttribute<RequirePermissionAttribute>());
        }
    }

    [Fact]
    public void ControllerAndContracts_DoNotAcceptTenantOrEmployeeFromBodyOrRoute()
    {
        var methods = ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => !x.IsSpecialName);
        Assert.DoesNotContain(methods.SelectMany(x => x.GetParameters()),
            x => x.Name is "tenantId" or "employeeId");
        Assert.DoesNotContain(typeof(RequestAttendanceCorrectionRequest).GetProperties(),
            x => x.Name is "TenantId" or "EmployeeId");
    }

    [Fact]
    public void ApplicationAssembly_DoesNotReferenceEfCore()
    {
        var refs = typeof(ONEVO.Application.DependencyInjection).Assembly
            .GetReferencedAssemblies().Select(x => x.Name).ToList();
        Assert.DoesNotContain(refs, x => x is "Microsoft.EntityFrameworkCore" or "Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public void ApprovalSnapshotMigration_AddsNonNullColumnAndBackfillsExistingRows()
    {
        var migrationsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ONEVO.Infrastructure", "Migrations"));
        var migration = Directory.GetFiles(migrationsDir, "*AddAttendanceCorrectionApprovalRequired.cs")
            .Single(x => !x.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var source = File.ReadAllText(migration);

        Assert.Contains("AddColumn<bool>", source);
        Assert.Contains("name: \"approval_required\"", source);
        Assert.Contains("nullable: false", source);
        Assert.Contains("SET approval_required = TRUE", source);
        Assert.Contains("status IN ('pending', 'rejected', 'cancelled')", source);
        Assert.Contains("reviewed_by_id IS NOT NULL", source);
        Assert.Contains("reviewed_at IS NOT NULL", source);
        Assert.DoesNotContain("tenant_isolation", source);
    }

    [Fact]
    public void Migration_DeclaresAttendanceCorrectionsRlsAndPendingUniqueIndex()
    {
        var migrationsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ONEVO.Infrastructure", "Migrations"));
        var migration = Directory.GetFiles(migrationsDir, "*AddAttendanceCorrections.cs")
            .Single(x => !x.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var source = File.ReadAllText(migration);
        Assert.Contains("TenantTables", source);
        Assert.Contains("attendance_corrections", source);
        Assert.Contains("tenant_isolation", source);
        Assert.Contains("status = 'pending'", source);
        Assert.Contains("ENABLE ROW LEVEL SECURITY", source);
    }

    private static void AssertPermission(string methodName, string expected)
    {
        var attribute = ControllerType.GetMethod(methodName)!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);
        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(expected, field!.GetValue(attribute));
    }
}
