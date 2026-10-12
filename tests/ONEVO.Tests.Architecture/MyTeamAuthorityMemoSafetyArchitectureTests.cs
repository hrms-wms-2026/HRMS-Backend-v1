using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Regression guard for the review clarification (2026-09-30): EmployeeAuthorityResolver keeps a
/// request-scoped memo of the expanded ManagementCoverageRecord expansion
/// (EmployeeAuthorityResolver._coverageMemo). That memo is safe only as long as nothing in the
/// same DI scope mutates coverage and then re-resolves visibility expecting the mutation to be
/// reflected immediately.
///
/// This is a static, same-file heuristic - not a full call-graph analysis - but it catches the
/// concrete failure mode: a single class that both writes ManagementCoverageRecords (directly, or
/// through one of the "*CoverageRecord*" Application commands) and also depends on
/// IEmployeeAuthorityResolver. As of this writing no such class exists (AddManualCoverageRecord/
/// UpdateManualCoverageRecord/RemoveManualCoverageRecordCommandHandler never reference the
/// resolver, and the resolver's own callers - ListEmployeesQueryHandler, ExceptionScopeResolver,
/// the attendance/device/location/work-area workflows, AttendanceReadHandlers,
/// AttendanceTodayStateService - never write coverage). If a future change needs both in one
/// scope, it must add an explicit invalidation call rather than relying on staleness being
/// harmless - see EmployeeAuthorityResolver._coverageMemo's doc comment.
/// </summary>
public sealed class MyTeamAuthorityMemoSafetyArchitectureTests
{
    private static readonly Regex MutatesCoverage = new(
        @"ManagementCoverageRecords\s*\.\s*(Add|Update|Remove)\s*\(" +
        @"|\b(Add|Update|Remove)(Manual)?CoverageRecord(Async)?\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void NoClass_MutatesManagementCoverage_AndAlsoDependsOnTheAuthorityResolver_InTheSameFile()
    {
        var applicationDir = FindDirectoryUnderRepoRoot("src", "ONEVO.Application");

        var offenders = Directory.EnumerateFiles(applicationDir, "*.cs", SearchOption.AllDirectories)
            // Scoped to Command handlers - the actual risk (a write, i.e. "mutation", followed by
            // a stale re-resolve) can only occur where a write happens: this codebase's CQRS
            // convention puts every write under a "Commands" folder. Repository interfaces and
            // implementations (RepositoryInterfaces/, Infrastructure/.../Repositories/) legitimately
            // declare both a coverage-mutation method AND get referenced in the resolver's own doc
            // comments without ever being the same "request" as a resolver call - they are data
            // access, not orchestration, so they are deliberately excluded here.
            .Where(file => file.Replace('\\', '/').Contains("/Commands/"))
            // The resolver's own implementation file legitimately mentions both - it IS the
            // authority, not a caller mutating coverage out from under itself.
            .Where(file => !file.Replace('\\', '/').Contains("/EmployeeAuthority/"))
            .Select(file => (Path: file, Text: File.ReadAllText(file)))
            .Where(x => MutatesCoverage.IsMatch(x.Text) && x.Text.Contains("IEmployeeAuthorityResolver", StringComparison.Ordinal))
            .Select(x => x.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files both mutate ManagementCoverageRecords and depend on IEmployeeAuthorityResolver. " +
            "If they run in the same request/DI scope as a coverage write, the resolver's request-scoped " +
            "memo (EmployeeAuthorityResolver._coverageMemo) can serve stale visibility after the write. " +
            "Either keep the mutation and the resolution in separate scopes, or add an explicit memo " +
            "invalidation after the write. Offending files: " + string.Join(", ", offenders));
    }

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
