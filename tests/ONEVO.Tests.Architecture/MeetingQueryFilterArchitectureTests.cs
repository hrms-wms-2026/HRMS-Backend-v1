using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.SharedPlatform.Jobs.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
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

    [Fact]
    public void MeetingEntities_AreTenantFilteredAndMapToTheSpecTables()
    {
        using var context = CreateModelInspectionContext();
        var expected = new Dictionary<Type, string>
        {
            [typeof(MeetingSeries)] = "meeting_series",
            [typeof(MeetingDetails)] = "meeting_details",
            [typeof(MeetingOccurrenceSuppression)] = "meeting_occurrence_suppressions",
            [typeof(MeetingOccurrenceAttendee)] = "meeting_occurrence_attendees",
            [typeof(MeetingSeriesLink)] = "meeting_series_links",
            [typeof(MeetingAttendanceSession)] = "meeting_attendance_sessions",
            [typeof(MeetingActionItem)] = "meeting_action_items",
        };

        foreach (var (clrType, table) in expected)
        {
            var entityType = context.Model.FindEntityType(clrType);
            Assert.NotNull(entityType);
            Assert.Equal(table, entityType!.GetTableName());
            Assert.True(entityType.GetDeclaredQueryFilters().Count > 0, $"{clrType.Name} has no tenant query filter");
        }

        var lease = context.Model.FindEntityType(typeof(JobLease));
        Assert.NotNull(lease);
        Assert.Equal("job_leases", lease!.GetTableName());
        Assert.Empty(lease.GetDeclaredQueryFilters()); // platform-level: no tenant scope, no RLS (spec 4.1)
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
    public static readonly string[] RawSqlOnTasksAllowList =
    [
        // Spec 5.6 occurrence insert: writes the meeting task itself (task_kind = meeting), never reads ordinary tasks.
        "EfMeetingOccurrenceWriter.cs",
    ];

    [Fact]
    public void NoBareIgnoreQueryFiltersOnWorkTasks_OutsideTheAllowList()
    {
        var offenders = SourceFiles()
            .SelectMany(file => MeetingQueryGuardScanner.MembersWithBareWorkTaskIgnore(File.ReadAllText(file))
                .Select(member => (Name: Path.GetFileName(file), Member: member)))
            .Where(site => !BareIgnoreAllowList.Contains(site.Name) && !BareIgnoreAllowList.Contains($"{site.Name}#{site.Member}"))
            .Select(site => $"{site.Name}#{site.Member}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>The files that declare the filter or compose it with the tenant filter; everything else that names it
    /// must apply MeetingVisibility.</summary>
    private static readonly string[] ExcludeMeetingsDeclarations = ["WorkTask.cs", "WorkTaskConfiguration.cs", "ApplicationDbContext.cs"];

    [Fact]
    public void IgnoringExcludeMeetings_IsOnlyInAllowListedFiles()
    {
        var offenders = SourceFiles()
            .Where(f => MeetingQueryGuardScanner.MentionsExcludeMeetings(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .Where(name => !ExcludeMeetingsDeclarations.Contains(name) && !ExcludeMeetingsIgnoreAllowList.Contains(name))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void RawSqlOnTasks_IsOnlyInAllowListedFiles()
    {
        var offenders = SourceFiles()
            .Where(f => MeetingQueryGuardScanner.HasRawSqlOnTasks(File.ReadAllText(f)))
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
