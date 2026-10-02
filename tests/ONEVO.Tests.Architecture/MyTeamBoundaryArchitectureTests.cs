using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>My Team spec §10: People I Manage vs Work I Lead are separate authorization domains.
/// WorkManagement must never read organizational coverage.</summary>
public sealed class MyTeamBoundaryArchitectureTests
{
    // Absolutely forbidden anywhere in WorkManagement, no exception: the raw coverage
    // closure/record tables, never just the facade.
    private static readonly string[] RawCoverageIdentifiers =
    [
        "IEmployeeVisibilityScopeResolver", "IEmployeeHierarchyClosureRepository", "ManagementCoverageRecord",
    ];

    // Dashboard/Team is allowed to go through IEmployeeAuthorityResolver - it is the one domain
    // that deliberately composes People I Manage and Work I Lead - but must never reach past that
    // facade into the raw coverage closure/record tables directly (My Team spec §10 rule 1).
    private static readonly string[] DashboardTeamRawCoverageIdentifiers =
    [
        "IEmployeeVisibilityScopeResolver", "IEmployeeHierarchyClosureRepository", "ManagementCoverageRecord",
    ];

    [Fact]
    public void WorkManagement_never_references_the_raw_coverage_tables()
    {
        var offenders = SourceFilesUnder("src", "ONEVO.Application", "Features", "WorkManagement")
            .Concat(SourceFilesUnder("src", "ONEVO.Infrastructure", "Persistence", "Repositories", "WorkManagement"))
            .Where(file => RawCoverageIdentifiers.Any(id =>
                File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//"))
                    .Any(line => line.Contains(id, StringComparison.Ordinal))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "WorkManagement code must never reference the raw organizational coverage tables (My Team spec §10). Offending files: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void WorkManagement_only_reaches_the_authority_resolver_through_Approvals()
    {
        // Approvals' WorkApprovalEngine resolves an HR-fallback approver (the requester's
        // reporting-line manager) when a Module has no hierarchy approver above it - a legitimate,
        // already-shipped cross into the facade, not the raw tables, mirroring the same exception
        // already granted to Dashboard/Team below. Every other WorkManagement area (Objectives,
        // Tasks, Sprints, Projects, Leadership, ...) still may never reference it at all.
        var offenders = SourceFilesUnder("src", "ONEVO.Application", "Features", "WorkManagement")
            .Concat(SourceFilesUnder("src", "ONEVO.Infrastructure", "Persistence", "Repositories", "WorkManagement"))
            .Where(file => !IsUnderApprovals(file))
            .Where(file =>
                File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//"))
                    .Any(line => line.Contains("IEmployeeAuthorityResolver", StringComparison.Ordinal)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Only WorkManagement/Approvals may reference IEmployeeAuthorityResolver (My Team spec §10). Offending files: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void DashboardTeam_never_reaches_past_the_authority_resolver_into_raw_coverage()
    {
        var offenders = SourceFilesUnder("src", "ONEVO.Application", "Features", "Dashboard", "Team")
            .Where(file => DashboardTeamRawCoverageIdentifiers.Any(id =>
                File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//"))
                    .Any(line => line.Contains(id, StringComparison.Ordinal))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Features/Dashboard/Team must only reach coverage through IEmployeeAuthorityResolver, "
            + "never the raw closure/coverage tables directly (My Team spec §10 rule 1). Offending files: "
            + string.Join(", ", offenders));
    }

    private static bool IsUnderApprovals(string file) =>
        file.Replace('\\', '/').Contains("/Features/WorkManagement/Approvals/", StringComparison.Ordinal);

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
