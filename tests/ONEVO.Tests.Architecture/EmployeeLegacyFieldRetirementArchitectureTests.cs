using System.Reflection;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the retirement of Employee.JobTitleId/Employee.ManagerId documented in
/// EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT.md. Reporting belongs to
/// positions/position_assignments (deferred), never to employees.manager_id, and job titles
/// are not modeled on Employee at all (no job_titles table exists).
/// </summary>
public sealed class EmployeeLegacyFieldRetirementArchitectureTests
{
    private static readonly Assembly DomainAssembly =
        typeof(ONEVO.Domain.Features.CoreHr.Entities.Employee).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    [Fact]
    public void EmployeeEntity_HasNoJobTitleIdProperty()
    {
        var property = typeof(ONEVO.Domain.Features.CoreHr.Entities.Employee)
            .GetProperty("JobTitleId");

        Assert.Null(property);
    }

    [Fact]
    public void EmployeeEntity_HasNoManagerIdProperty()
    {
        var property = typeof(ONEVO.Domain.Features.CoreHr.Entities.Employee)
            .GetProperty("ManagerId");

        Assert.Null(property);
    }

    [Fact]
    public void EmployeeConfiguration_NoLongerConfiguresManagerSelfForeignKey()
    {
        var source = ReadSourceRelativeToRepoRoot(
            "src", "ONEVO.Infrastructure", "Persistence", "Configurations",
            "CoreHr", "Employee", "EmployeeConfiguration.cs");

        Assert.DoesNotContain("ManagerId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JobTitleId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainAssembly_HasNoJobTitleType()
    {
        var offenders = DomainAssembly.GetTypes()
            .Where(t => t.Name.Contains("JobTitle", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "No JobTitle entity/type may exist (job_titles table is intentionally not modeled): " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void NoApplicationFeature_ReferencesEmployeeManagerIdOrJobTitleId()
    {
        var sourceRoot = FindRepositoryPath("src", "ONEVO.Application");
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("ManagerId", StringComparison.Ordinal) ||
                       text.Contains("JobTitleId", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(offenders.Count == 0,
            "No Application feature may reference Employee.ManagerId/JobTitleId (retired): " +
            string.Join("; ", offenders));

        // Touch the assembly reference so the field stays exercised if this test is ever trimmed.
        Assert.NotNull(ApplicationAssembly);
    }

    private static string ReadSourceRelativeToRepoRoot(params string[] segments) =>
        File.ReadAllText(FindRepositoryPath(segments));

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
