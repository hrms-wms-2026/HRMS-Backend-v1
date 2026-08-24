using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.TimeTracking;
using ONEVO.Api.Controllers.Tenant.Attendance;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class TimeTrackingMutationArchitectureTests
{
    private static readonly Type ControllerType = typeof(TimeTrackingController);

    [Fact]
    public void Controller_IsTenantAuthenticated_AndUsesExpectedRoute()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.Attendance", ControllerType.Namespace);
        Assert.Equal("TenantPolicy", ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal(
            "api/v1/attendance/time-tracking",
            ControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void MutationRoutes_ExistWithoutManagementPermissionAttributes()
    {
        var mutationMethods = new[]
        {
            (ControllerType.GetMethod(nameof(TimeTrackingController.ClockIn))!, "clock-in"),
            (ControllerType.GetMethod(nameof(TimeTrackingController.ClockOut))!, "clock-out"),
            (ControllerType.GetMethod(nameof(TimeTrackingController.StartBreak))!, "break/start"),
            (ControllerType.GetMethod(nameof(TimeTrackingController.EndBreak))!, "break/end")
        };

        foreach (var (method, route) in mutationMethods)
        {
            Assert.Equal(route, method.GetCustomAttribute<HttpPostAttribute>()?.Template);
            Assert.Null(method.GetCustomAttributes()
                .SingleOrDefault(x => x.GetType().Name == "RequirePermissionAttribute"));
        }
    }

    [Fact]
    public void MutationActions_DoNotAcceptTenantOrEmployeeIdentifiers()
    {
        var parameters = new[]
        {
            ControllerType.GetMethod(nameof(TimeTrackingController.ClockIn))!,
            ControllerType.GetMethod(nameof(TimeTrackingController.ClockOut))!,
            ControllerType.GetMethod(nameof(TimeTrackingController.StartBreak))!,
            ControllerType.GetMethod(nameof(TimeTrackingController.EndBreak))!
        }
        .SelectMany(method => method.GetParameters());

        Assert.DoesNotContain(parameters, parameter =>
            parameter.Name is "tenantId" or "employeeId" or "legalEntityId");
    }

    [Fact]
    public void RequestContracts_ContainOnlySupportedClientFields()
    {
        var clockInProperties = typeof(ClockInRequest).GetProperties().Select(property => property.Name).ToArray();
        Assert.Equal(["Source"], clockInProperties);
        Assert.Empty(typeof(ClockOutRequest).GetProperties());
        Assert.Empty(typeof(StartBreakRequest).GetProperties());
        Assert.Empty(typeof(EndBreakRequest).GetProperties());
    }

    [Fact]
    public void ApplicationAssembly_DoesNotReferenceEfCore()
    {
        var references = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Npgsql.EntityFrameworkCore.PostgreSQL", references);
    }

    [Fact]
    public void AttendanceRepository_UsesTrackedFetchForMutation()
    {
        var path = FindRepositorySource();
        var source = File.ReadAllText(path);
        var trackedMethod = source[(source.IndexOf("GetTrackedRecordAsync", StringComparison.Ordinal))..];
        trackedMethod = trackedMethod[..trackedMethod.IndexOf("public async Task<IReadOnlyList<AttendanceRecord>>", StringComparison.Ordinal)];

        Assert.DoesNotContain("AsNoTracking", trackedMethod);
        Assert.Contains("db.AttendanceRecords", trackedMethod);
    }

    [Fact]
    public void BreakMigration_AddsOnlyTheFilteredUniqueOpenBreakIndex()
    {
        var migrationsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ONEVO.Infrastructure", "Migrations"));
        var migrationPath = Directory.GetFiles(migrationsDir, "*AddBreakRecordOpenUniqueness.cs")
            .Single(file => !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var migration = File.ReadAllText(migrationPath);
        var designer = File.ReadAllText(Path.ChangeExtension(migrationPath, ".Designer.cs"));
        var snapshot = File.ReadAllText(Path.Combine(migrationsDir, "ApplicationDbContextModelSnapshot.cs"));

        Assert.Contains("CreateIndex", migration);
        Assert.Contains("ux_break_records_one_open_per_employee", migration);
        Assert.Contains("unique: true", migration);
        Assert.Contains("filter: \"break_end IS NULL\"", migration);
        Assert.DoesNotContain("CreateTable", migration);
        Assert.Contains("ux_break_records_one_open_per_employee", designer);
        Assert.Contains("Property<uint>(\"xmin\")", designer);
        Assert.Contains("ux_break_records_one_open_per_employee", snapshot);
        Assert.Contains("Property<uint>(\"xmin\")", snapshot);
    }

    [Fact]
    public void AttendanceMigration_StillEnablesForcedTenantRls()
    {
        var migrationsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ONEVO.Infrastructure", "Migrations"));
        var migration = Directory.GetFiles(migrationsDir, "*AddAttendanceReadModel.cs")
            .Single(file => !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var source = File.ReadAllText(migration);

        Assert.Contains("ENABLE ROW LEVEL SECURITY", source);
        Assert.Contains("FORCE ROW LEVEL SECURITY", source);
        Assert.Contains("tenant_isolation", source);
        Assert.Contains("attendance_records", source);
        Assert.Contains("break_records", source);
    }

    private static string FindRepositorySource()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(
            root,
            "src",
            "ONEVO.Infrastructure",
            "Persistence",
            "Repositories",
            "TimeAttendance",
            "EfAttendanceReadRepository.cs");
    }
}
