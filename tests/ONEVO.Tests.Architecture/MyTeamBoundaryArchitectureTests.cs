using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>My Team spec §10: People I Manage vs Work I Lead are separate authorization domains.
/// WorkManagement must never read organizational coverage.</summary>
public sealed class MyTeamBoundaryArchitectureTests
{
    private static readonly string[] CoverageIdentifiers =
    [
        "IEmployeeAuthorityResolver", "IEmployeeVisibilityScopeResolver",
        "IEmployeeHierarchyClosureRepository", "ManagementCoverageRecord",
    ];

    [Fact]
    public void WorkManagement_never_references_management_coverage()
    {
        var offenders = SourceFilesUnder("src", "ONEVO.Application", "Features", "WorkManagement")
            .Concat(SourceFilesUnder("src", "ONEVO.Infrastructure", "Persistence", "Repositories", "WorkManagement"))
            .Where(file => CoverageIdentifiers.Any(id =>
                File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//"))
                    .Any(line => line.Contains(id, StringComparison.Ordinal))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "WorkManagement code must never reference organizational coverage (My Team spec §10). Offending files: "
            + string.Join(", ", offenders));
    }

    internal static IEnumerable<string> SourceFilesUnder(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. segments]);
            if (Directory.Exists(candidate))
                return Directory.EnumerateFiles(candidate, "*.cs", SearchOption.AllDirectories);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(Path.Combine(segments));
    }
}
