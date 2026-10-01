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

    // Dashboard/Team is allowed to go through IEmployeeAuthorityResolver - it is the one domain
    // that deliberately composes People I Manage and Work I Lead - but must never reach past that
    // facade into the raw coverage closure/record tables directly (My Team spec §10 rule 1).
    private static readonly string[] RawCoverageIdentifiers =
    [
        "IEmployeeVisibilityScopeResolver", "IEmployeeHierarchyClosureRepository", "ManagementCoverageRecord",
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

    [Fact]
    public void DashboardTeam_never_reaches_past_the_authority_resolver_into_raw_coverage()
    {
        var offenders = SourceFilesUnder("src", "ONEVO.Application", "Features", "Dashboard", "Team")
            .Where(file => RawCoverageIdentifiers.Any(id =>
                File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//"))
                    .Any(line => line.Contains(id, StringComparison.Ordinal))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Features/Dashboard/Team must only reach coverage through IEmployeeAuthorityResolver, "
            + "never the raw closure/coverage tables directly (My Team spec §10 rule 1). Offending files: "
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
