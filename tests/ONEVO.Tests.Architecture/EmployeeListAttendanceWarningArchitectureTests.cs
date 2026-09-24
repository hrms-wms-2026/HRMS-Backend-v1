namespace ONEVO.Tests.Architecture;

public sealed class EmployeeListAttendanceWarningArchitectureTests
{
    [Fact]
    public void ListEmployeesQueryHandler_DoesNotInvokeTodayStatePerEmployee()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "CoreHr", "Employee", "Queries", "ListEmployees", "ListEmployeesQueryHandler.cs"));

        Assert.DoesNotContain("IAttendanceTodayStateService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTodayAsync", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeListAttendanceOptions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeListRepository_UsesNoTrackingBatchAttendanceRead()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "CoreHr", "EfEmployeeRepository.cs"));

        Assert.Contains("_db.AttendanceRecords.AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("employeeIds.Contains(record.EmployeeId)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTodayAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeListApplicationCode_HasNoEntityFrameworkDependency()
    {
        var applicationRoot = FindRepositoryPath("src", "ONEVO.Application");
        var offendingFile = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.ReadAllText(path).Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        Assert.Null(offendingFile);
    }

    [Fact]
    public void ExistingEmployeesController_RemainsTheOnlyEmployeeListApiSurface()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs"));

        Assert.Contains("[HttpGet]", source, StringComparison.Ordinal);
        Assert.Contains("List(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("not-clocked-in", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EmployeeListWarning", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
