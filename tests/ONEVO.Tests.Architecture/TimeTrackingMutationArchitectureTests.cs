using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.TimeTracking;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
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

    /// <summary>
    /// This used to assert tracked-vs-no-tracking behavior by slicing the repository's source text
    /// between two literal method-name strings, which threw ArgumentOutOfRangeException the moment
    /// method order/signatures shifted (e.g. ListRecordsAsync's tuple return type no longer matches
    /// the literal it searched for) - a brittle source-layout assertion unrelated to whether tracked
    /// mutation actually works. The real tracked-vs-detached-Update() behavior is now proven
    /// directly against a database in
    /// EfAttendanceReadRepositoryTests.GetTrackedRecordAsync_ReturnsTrackedEntity_AndMutationPersistsViaSaveChanges.
    /// This test instead checks only the stable architectural contract: the abstraction mutation
    /// handlers depend on exposes a tracked-fetch method with the expected shape.
    /// </summary>
    [Fact]
    public void AttendanceRepository_ExposesTrackedFetchForMutation()
    {
        var method = typeof(IAttendanceReadRepository).GetMethod(nameof(IAttendanceReadRepository.GetTrackedRecordAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AttendanceRecord>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(Guid), parameters[0].ParameterType);
        Assert.Equal(typeof(Guid), parameters[1].ParameterType);
        Assert.Equal(typeof(DateOnly), parameters[2].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
    }

    [Theory]
    [InlineData(typeof(ONEVO.Application.Features.TimeAttendance.Commands.ClockIn.ClockInCommandHandler))]
    [InlineData(typeof(ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests.WorkAreaChangeRequestWorkflow))]
    public void MutationHandlers_DependOnAttendanceRepositoryAbstraction(Type handlerType)
    {
        var constructor = handlerType.GetConstructors().Single();
        Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IAttendanceReadRepository));
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
}
