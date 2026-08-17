using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards PositionAssignment against reintroducing the retired manager/job-title fields
/// (see EmployeeLegacyFieldRetirementArchitectureTests) and confirms it does not depend on
/// the Api layer.
/// </summary>
public sealed class PositionAssignmentArchitectureTests
{
    [Fact]
    public void PositionAssignment_HasNoManagerIdProperty()
    {
        var property = typeof(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment)
            .GetProperty("ManagerId");

        Assert.Null(property);
    }

    [Fact]
    public void PositionAssignment_HasNoJobTitleIdProperty()
    {
        var property = typeof(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment)
            .GetProperty("JobTitleId");

        Assert.Null(property);
    }

    [Fact]
    public void PositionAssignment_HasExpectedShape()
    {
        var type = typeof(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment);

        Assert.NotNull(type.GetProperty("EmployeeId"));
        Assert.NotNull(type.GetProperty("PositionId"));
        Assert.NotNull(type.GetProperty("AssignmentKind"));
        Assert.NotNull(type.GetProperty("EffectiveFrom"));
        Assert.NotNull(type.GetProperty("EffectiveTo"));
        Assert.NotNull(type.GetProperty("AssignmentStatus"));
    }

    [Fact]
    public void DomainAssembly_HasNoOtherPositionAssignmentManagerOrJobTitleReferences()
    {
        var offenders = new[] { "ONEVO.Application", "ONEVO.Infrastructure" }
            .SelectMany(project => Directory.GetFiles(
                FindRepositoryPath("src", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Replace('\\', '/').Contains("/Migrations/", StringComparison.Ordinal))
            .Where(path => path.Contains("PositionAssignment", StringComparison.Ordinal))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("ManagerId", StringComparison.Ordinal) ||
                       text.Contains("JobTitleId", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(offenders.Count == 0,
            "No PositionAssignment-related code may reference ManagerId/JobTitleId: " +
            string.Join("; ", offenders));
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
