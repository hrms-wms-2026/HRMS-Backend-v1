public sealed class AttendanceStatusLeaveAwarenessArchitectureTests
{
    [Fact]
    public void ApplicationAttendanceCode_HasNoEntityFrameworkDependency()
    {
        var applicationRoot = FindRepositoryPath("src", "ONEVO.Application");
        var offendingFile = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.ReadAllText(path).Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        Assert.Null(offendingFile);
    }

    [Fact]
    public void ClockInHandler_DoesNotBlockNonWorkingDays()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "TimeAttendance", "Commands", "ClockIn", "ClockInCommandHandler.cs"));

        Assert.DoesNotContain("off_day", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedWorkingDay = context.Schedule.IsWorkingDay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovedLeaveRepository_UsesTenantScopedNoTrackingOverlapQuery()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "Leave", "Request", "EfLeaveRequestReadRepository.cs"));

        Assert.Contains("AsNoTracking", source, StringComparison.Ordinal);
        Assert.Contains("request.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.Contains("request.Status == LeaveRequestStatuses.Approved", source, StringComparison.Ordinal);
        Assert.Contains("request.StartAt < toExclusive", source, StringComparison.Ordinal);
        Assert.Contains("request.EndAt > fromStart", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovedLeaveRepository_IsRegisteredInInfrastructure()
    {
        var source = ReadInfrastructureRegistrations(FindRepositoryPath("src", "ONEVO.Infrastructure", "DependencyInjection.cs"));

        Assert.Contains("ILeaveRequestReadRepository", source, StringComparison.Ordinal);
        Assert.Contains("EfLeaveRequestReadRepository", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
                return Path.Combine([directory.FullName, .. segments]);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    /// <summary>AddInfrastructure and every per-area registration file it calls (task 25 split them into Registration/).</summary>
    private static string ReadInfrastructureRegistrations(string dependencyInjectionPath)
    {
        var registrationDirectory = Path.Combine(Path.GetDirectoryName(dependencyInjectionPath)!, "Registration");
        var files = new[] { dependencyInjectionPath }
            .Concat(Directory.Exists(registrationDirectory)
                ? Directory.GetFiles(registrationDirectory, "*.cs").OrderBy(f => f, StringComparer.Ordinal)
                : []);
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }
}
