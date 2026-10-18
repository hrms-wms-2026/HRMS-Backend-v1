using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>Meeting occurrences are tasks (task_kind = 'meeting') that ordinary task queries must never see
/// (meetings-as-tasks Phase 2 spec 2.2, 5.1).</summary>
public sealed class MeetingQueryFilterArchitectureTests
{
    [Fact]
    public void WorkTask_DeclaresTheTenantScopeAndExcludeMeetingsFiltersByName()
    {
        using var context = CreateModelInspectionContext();

        var filters = context.Model.FindEntityType(typeof(WorkTask))!.GetDeclaredQueryFilters();

        Assert.Contains(filters, f => f.Key == WorkTaskQueryFilters.ExcludeMeetings);
        Assert.Contains(filters, f => f.Key == WorkTaskQueryFilters.TenantScope);
        Assert.DoesNotContain(filters, f => f.Key is null); // never mix anonymous and named filters
    }

    /// <summary>Bare IgnoreQueryFilters() on WorkTasks also drops ExcludeMeetings. Only these may do it: each counts
    /// meeting rows on purpose (a status or category a meeting task references must stay undeletable - FK safety),
    /// or is dev seed data.</summary>
    public static readonly string[] BareIgnoreAllowList =
    [
        "EfWorkTaskRepository.cs#AnyActiveByStatusIdAsync",
        "EfWorkTaskRepository.cs#AnyActiveByCategoryIdAsync",
        "DapiDueTodayTaskSeeder.cs",
        "WorkManagementDapiDemoSeeder.Tasks.cs",
    ];

    /// <summary>Files that ignore ExcludeMeetings; each must also apply MeetingVisibility.ForCaller (spec 5.1).</summary>
    public static readonly string[] ExcludeMeetingsIgnoreAllowList = [];

    /// <summary>Files whose raw SQL touches the tasks table, so it bypasses every EF query filter.</summary>
    public static readonly string[] RawSqlOnTasksAllowList = [];

    [Fact]
    public void NoBareIgnoreQueryFiltersOnWorkTasks_OutsideTheAllowList()
    {
        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            var statements = Statements(File.ReadAllText(file));
            foreach (var (statement, member) in statements)
            {
                if (!statement.Contains("WorkTasks") || !statement.Contains(".IgnoreQueryFilters()"))
                    continue;
                var name = Path.GetFileName(file);
                if (BareIgnoreAllowList.Contains(name) || BareIgnoreAllowList.Contains($"{name}#{member}"))
                    continue;
                offenders.Add($"{name}#{member}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void IgnoringExcludeMeetings_IsOnlyInAllowListedFiles()
    {
        var offenders = SourceFiles()
            .Where(f => File.ReadAllText(f) is var s
                && (s.Contains("IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings") || s.Contains("\"ExcludeMeetings\"")))
            .Select(Path.GetFileName)
            .Where(name => name is not "WorkTask.cs" && !ExcludeMeetingsIgnoreAllowList.Contains(name))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void RawSqlOnTasks_IsOnlyInAllowListedFiles()
    {
        var rawSql = new Regex(@"(ExecuteSql\w*|SqlQuery\w*|FromSql\w*)\s*\(\s*\$?@?""""?""?[^;]*?\btasks\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var offenders = SourceFiles()
            .Where(f => rawSql.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .Where(name => !RawSqlOnTasksAllowList.Contains(name))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(FindRepositoryPath("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Each ';'-terminated statement with the name of the member it sits in (the last "Name(" seen on a
    /// line starting with "public"/"private"/"internal"/"protected").</summary>
    private static IEnumerable<(string Statement, string Member)> Statements(string source)
    {
        var member = "";
        var current = new System.Text.StringBuilder();
        foreach (var line in source.Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*(public|private|internal|protected)\b[^=(]*?\b(\w+)\s*(<[^>]*>)?\s*\(");
            if (match.Success)
                member = match.Groups[2].Value;
            current.Append(line).Append('\n');
            if (line.Contains(';'))
            {
                yield return (current.ToString(), member);
                current.Clear();
            }
        }
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
                return Path.Combine([directory.FullName, .. segments]);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"arch-test-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
