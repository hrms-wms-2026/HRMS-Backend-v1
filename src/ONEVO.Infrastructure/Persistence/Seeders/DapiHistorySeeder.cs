using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only, idempotent rolling history for the Dapi demo tenant: the last <see cref="HistoryDays"/> days up to
/// today (Asia/Colombo), recomputed from the clock on every boot, so the app always looks like it has been used for two
/// months straight. The demo is shown as the tenant owner, so everything is laid out to be seen from the top of the
/// reporting line: the owner's own day plus every report below them.
///
/// Covers attendance (clock in/out, presence, lunch and short breaks), monitoring (activity, app usage, device state,
/// daily summaries, face checks), leave (decided history and pending requests waiting on approvers), project work
/// (two-week sprints, tasks opened week by week with status, progress and edit history, comments), the planner
/// (task blocks, task clocking sessions, team stand-ups and a leadership sync) and exception cases with their chats,
/// escalations and inbox activity. Every row has a deterministic id, so re-running only fills the days that are new
/// since the last boot. Must run after the Dapi tenant, org structure, Work Management demo and leave seeders.
/// </summary>
public sealed partial class DapiHistorySeeder : IHostedService
{
    internal static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    internal static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    internal static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    /// <summary>Gets no seeded attendance (records, presence, breaks), so Clock In / Clock Out can be tried live as
    /// this person; earlier seeded rows for them are removed. Their leave and monitoring history stay.</summary>
    internal const string ClockInTesterEmail = "tharmi.rajendran@dapi.test";

    /// <summary>How far back the history reaches from today.</summary>
    internal const int HistoryDays = 60;

    internal const string ColomboZoneId = "Asia/Colombo";
    private static readonly TimeSpan ColomboOffset = TimeSpan.FromMinutes(330);

    private static readonly (string ProcessName, string Category)[] SeedApplications =
    [
        ("chrome.exe", GlobalApplicationCategories.Browser),
        ("ms-teams.exe", GlobalApplicationCategories.Meeting),
        ("code.exe", GlobalApplicationCategories.Development),
        ("winword.exe", GlobalApplicationCategories.Documentation),
        ("excel.exe", GlobalApplicationCategories.Documentation),
        ("outlook.exe", GlobalApplicationCategories.Communication),
        ("slack.exe", GlobalApplicationCategories.Communication),
        ("figma.exe", GlobalApplicationCategories.Design),
        ("postman.exe", GlobalApplicationCategories.Development),
        ("notion.exe", GlobalApplicationCategories.Productivity)
    ];

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DapiHistorySeeder> _logger;

    public DapiHistorySeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<DapiHistorySeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return;

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

            tenantContext.SetAdminMode();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(6));

            // The real routes (coverage, reporting line, HR fallback, delegation). A hosted service has no signed-in
            // user, so the authority resolver is pinned to the Dapi tenant instead of read from ICurrentUser.
            var authority = scope.ServiceProvider.GetRequiredService<IEmployeeAuthorityResolverFactory>().CreateForTenant(DapiTenantId);
            var approvers = ActivatorUtilities.CreateInstance<LeaveApproverResolver>(scope.ServiceProvider, authority);
            var employeeCount = await SeedAsync(
                db, tenantContext, timeout.Token, approvers, authority, scope.ServiceProvider, DateTimeOffset.UtcNow);
            _logger.LogInformation(
                "Dapi rolling {Days}-day history seeded for {EmployeeCount} employees.", HistoryDays, employeeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dapi rolling history seeding failed. Startup will continue.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <param name="services">The seeding scope, for the meeting writer; meetings are skipped without it.</param>
    internal static async Task<int> SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        CancellationToken ct,
        ILeaveApproverResolver? approvers = null,
        IEmployeeAuthorityResolver? authority = null,
        IServiceProvider? services = null,
        DateTimeOffset? utcNow = null)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (tenant is null)
            return 0;

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null));

        var employees = await db.Employees
            .Where(e => e.TenantId == DapiTenantId
                && e.LegalEntityId == DapiLegalEntityId
                && !e.IsDeleted
                && e.TerminationDate == null)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(ct);
        if (employees.Count == 0)
            return 0;

        var owner = employees.FirstOrDefault(e => e.UserId == DapiOwnerUserId) ?? employees[0];
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var window = HistoryWindow.For(now);
        var ctx = new SeedContext(db, employees, owner, window, now, approvers, authority, services);
        await ctx.LoadManagersAsync(ct);

        await EnsureGlobalApplicationsAsync(db, window, ct);

        // The leave plan is pure, so attendance knows the approved days off before any leave row is written.
        var leaveTypes = await db.LeaveTypes
            .Where(t => t.TenantId == DapiTenantId && t.IsActive)
            .OrderBy(t => t.Code)
            .ToListAsync(ct);
        var leavePlans = PlanLeave(ctx, leaveTypes);
        var approvedDays = leavePlans
            .Where(p => p.Status == LeaveRequestStatuses.Approved)
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Date).ToHashSet());

        await SeedAttendanceAndMonitoringAsync(ctx, approvedDays, ct);
        await SeedLeaveAsync(ctx, leavePlans, ct);

        await SeedWorkHistoryAsync(ctx, ct);
        await SeedPlannerAsync(ctx, approvedDays, ct);
        await SeedExceptionCasesAsync(ctx, ct);
        // Last: it goes through the app's meeting writer (raw SQL, series locks); a failure there leaves the rest seeded.
        await SeedMeetingsAsync(ctx, ct);
        return employees.Count;
    }

    private static async Task EnsureGlobalApplicationsAsync(ApplicationDbContext db, HistoryWindow window, CancellationToken ct)
    {
        var processNames = SeedApplications.Select(x => x.ProcessName).ToArray();
        var existing = (await db.GlobalApplications
            .Where(x => processNames.Contains(x.ProcessName))
            .ToListAsync(ct))
            .ToDictionary(x => x.ProcessName, StringComparer.OrdinalIgnoreCase);
        var firstSeen = At(window.From, 9, 0);
        var lastSeen = At(window.Today, 9, 0);

        foreach (var (processName, category) in SeedApplications)
        {
            if (existing.TryGetValue(processName, out var application))
            {
                if (application.FirstSeenAt > firstSeen)
                    application.FirstSeenAt = firstSeen;
                if (application.LastSeenAt < lastSeen)
                    application.LastSeenAt = lastSeen;
                continue;
            }

            db.GlobalApplications.Add(new GlobalApplication
            {
                Id = SeedId($"global-app:{processName}"),
                AppName = GlobalApplication.DefaultDisplayName(processName),
                ProcessName = processName,
                Category = category,
                IsPublic = true,
                IsProductiveDefault = category != GlobalApplicationCategories.Meeting,
                FirstSeenAt = firstSeen,
                LastSeenAt = lastSeen,
                CreatedAt = firstSeen
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<HashSet<Guid>> ExistingIdsAsync(IQueryable<Guid> query, CancellationToken ct) =>
        (await query.ToListAsync(ct)).ToHashSet();

    /// <summary>Kept on the original namespace so rows seeded by the older fixed-month version keep their ids.</summary>
    private static Guid SeedId(string key) =>
        WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-september-2026:{key}");

    /// <summary>A stable 0..(max-1) pick for a key, so variety never changes between boots.</summary>
    private static int Pick(string key, int max)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)max);
    }

    private static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, ColomboOffset)
            .AddHours(hour).AddMinutes(minute).ToUniversalTime();

    private static bool IsWorkday(DateOnly date) => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static string HashTitle(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>The seeded span: <see cref="From"/>..yesterday is full history, <see cref="Today"/> is filled up to now.</summary>
    internal sealed record HistoryWindow(DateOnly From, DateOnly Today)
    {
        public static HistoryWindow For(DateTimeOffset utcNow)
        {
            var today = DateOnly.FromDateTime(utcNow.ToOffset(ColomboOffset).DateTime);
            return new HistoryWindow(today.AddDays(-HistoryDays), today);
        }

        public IEnumerable<DateOnly> PastDays()
        {
            for (var d = From; d < Today; d = d.AddDays(1))
                yield return d;
        }

        public IReadOnlyList<DateOnly> PastWorkdays() => PastDays().Where(IsWorkday).ToList();
    }

    /// <summary>What every part of the seeder shares: the people, who manages whom, and the clock.</summary>
    private sealed class SeedContext(
        ApplicationDbContext db,
        IReadOnlyList<Employee> employees,
        Employee owner,
        HistoryWindow window,
        DateTimeOffset now,
        ILeaveApproverResolver? approvers,
        IEmployeeAuthorityResolver? authority,
        IServiceProvider? services)
    {
        public ApplicationDbContext Db { get; } = db;
        public IReadOnlyList<Employee> Employees { get; } = employees;
        public Employee Owner { get; } = owner;
        public HistoryWindow Window { get; } = window;
        public DateTimeOffset Now { get; } = now;
        public ILeaveApproverResolver? Approvers { get; } = approvers;
        public IEmployeeAuthorityResolver? Authority { get; } = authority;
        public IServiceProvider? Services { get; } = services;

        /// <summary>Direct manager per employee, from the reporting-line closure (depth 1).</summary>
        public Dictionary<Guid, Guid> ManagerOf { get; } = new();

        public Employee? ClockInTester { get; } = employees.FirstOrDefault(
            e => string.Equals(e.Email, ClockInTesterEmail, StringComparison.OrdinalIgnoreCase));

        public int IndexOf(Employee employee)
        {
            for (var i = 0; i < Employees.Count; i++)
                if (Employees[i].Id == employee.Id)
                    return i;
            return 0;
        }

        public Guid ManagerOrOwner(Guid employeeId) =>
            ManagerOf.TryGetValue(employeeId, out var manager) ? manager
                : employeeId == Owner.Id ? Employees.First(e => e.Id != Owner.Id).Id : Owner.Id;

        public async Task LoadManagersAsync(CancellationToken ct)
        {
            var rows = await Db.EmployeeHierarchyClosures
                .Where(c => c.TenantId == DapiTenantId && c.Depth == 1)
                .Select(c => new { c.AncestorEmployeeId, c.DescendantEmployeeId })
                .ToListAsync(ct);
            foreach (var row in rows)
                ManagerOf.TryAdd(row.DescendantEmployeeId, row.AncestorEmployeeId);
        }
    }
}
