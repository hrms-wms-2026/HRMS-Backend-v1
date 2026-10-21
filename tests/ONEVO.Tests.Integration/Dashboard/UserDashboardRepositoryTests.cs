using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Dashboard;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Dashboard;

/// <summary>One cloned, fully migrated Postgres database shared by the facts of the class. Each fact uses its own tenant/user ids.</summary>
public sealed class UserDashboardRepositoryTestsFixture : IAsyncLifetime
{
    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync() => _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}

/// <summary>
/// Proves EfUserDashboardRepository and the layout handlers' persistence patterns (soft-delete removal, row reuse,
/// optimistic version) against real PostgreSQL: partial unique indexes WHERE is_deleted = false, the unique violation
/// race on seeding, and the layout_version concurrency token. Requires Docker (like the other Testcontainers suites).
/// The connection is the cloned database's superuser, so RLS is bypassed: tenant scoping is verified through the
/// repository's explicit tenant predicate.
/// </summary>
public sealed class UserDashboardRepositoryTests : IClassFixture<UserDashboardRepositoryTestsFixture>
{
    private const string Key = DashboardKeys.MyWork;
    private readonly UserDashboardRepositoryTestsFixture _fixture;

    public UserDashboardRepositoryTests(UserDashboardRepositoryTestsFixture fixture) => _fixture = fixture;

    private static UserDashboard NewDashboard(Guid tenantId, Guid userId) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        DashboardKey = Key,
        Widgets = DashboardSeeder.BuildDefaultWidgets(tenantId, Key),
    };

    private async Task<UserDashboard> SeedAsync(Guid tenantId, Guid userId)
    {
        using var db = _fixture.CreateContext();
        var dashboard = NewDashboard(tenantId, userId);
        (await new EfUserDashboardRepository(db).TryAddAsync(dashboard)).Should().BeTrue();
        return dashboard;
    }

    private static DashboardWidget NewWidget(Guid tenantId, string key)
    {
        var widget = new DashboardWidget { TenantId = tenantId, WidgetKey = key };
        foreach (var bp in Breakpoints.All)
            widget.Layouts.Add(new DashboardWidgetLayout { TenantId = tenantId, Breakpoint = bp, X = 0, Y = 90, W = 2, H = 2 });
        return widget;
    }

    [Fact]
    public async Task TryAddAsync_SeedsDefaultWidgetsWithThreeLayoutsEachAndTenantId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expectedKeys = DashboardSeeder.BuildDefaultWidgets(tenantId, Key).Select(w => w.WidgetKey).ToList();
        await SeedAsync(tenantId, userId);

        using var db = _fixture.CreateContext();
        var loaded = await new EfUserDashboardRepository(db).GetAsync(tenantId, userId, Key);

        loaded.Should().NotBeNull();
        loaded!.TenantId.Should().Be(tenantId);
        loaded.LayoutVersion.Should().Be(1);
        loaded.Widgets.Select(w => w.WidgetKey).Should().BeEquivalentTo(expectedKeys);
        loaded.Widgets.Should().OnlyContain(w => w.TenantId == tenantId && w.Layouts.Count == 3);
        loaded.Widgets.SelectMany(w => w.Layouts).Should().OnlyContain(l => l.TenantId == tenantId);
        loaded.Widgets.All(w => w.Layouts.Select(l => l.Breakpoint).OrderBy(b => b)
            .SequenceEqual(Breakpoints.All.OrderBy(b => b))).Should().BeTrue();
    }

    [Fact]
    public async Task TryAddAsync_SecondInsertForSameTenantUserKey_ReturnsFalseAndFirstWins()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var first = await SeedAsync(tenantId, userId);

        using var db2 = _fixture.CreateContext();
        var repo2 = new EfUserDashboardRepository(db2);
        var loser = NewDashboard(tenantId, userId);
        (await repo2.TryAddAsync(loser)).Should().BeFalse();

        // The losing context is usable again and sees the winner's rows, not its own detached graph.
        var reread = await repo2.GetAsync(tenantId, userId, Key);
        reread!.Id.Should().Be(first.Id);
        reread.Id.Should().NotBe(loser.Id);

        using var verify = _fixture.CreateContext();
        (await verify.UserDashboards.CountAsync(d => d.TenantId == tenantId && d.UserId == userId)).Should().Be(1);
        (await verify.DashboardWidgets.CountAsync(w => w.TenantId == tenantId)).Should().Be(first.Widgets.Count);
    }

    [Fact]
    public async Task SaveWithVersionAsync_RemovedWidget_IsExcludedAndSameKeyCanBeReAdded()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var seeded = await SeedAsync(tenantId, userId);
        var removedKey = seeded.Widgets[0].WidgetKey;
        var total = seeded.Widgets.Count;

        using (var db = _fixture.CreateContext())
        {
            var repo = new EfUserDashboardRepository(db);
            var dashboard = (await repo.GetAsync(tenantId, userId, Key))!;
            var widget = dashboard.Widgets.Single(w => w.WidgetKey == removedKey);
            DashboardLayoutSupport.RemoveWidget(dashboard, widget);
            (await repo.SaveWithVersionAsync(dashboard, 1)).Should().BeTrue();
        }

        using (var db = _fixture.CreateContext())
        {
            var reloaded = (await new EfUserDashboardRepository(db).GetAsync(tenantId, userId, Key))!;
            reloaded.LayoutVersion.Should().Be(2);
            reloaded.Widgets.Should().HaveCount(total - 1);
            reloaded.Widgets.Should().NotContain(w => w.WidgetKey == removedKey);

            // Soft deleted, not physically removed (widget and its layouts).
            (await db.DashboardWidgets.IgnoreQueryFilters().CountAsync(w => w.DashboardId == reloaded.Id && w.WidgetKey == removedKey && w.IsDeleted))
                .Should().Be(1);
            (await db.DashboardWidgetLayouts.IgnoreQueryFilters().CountAsync(l => l.TenantId == tenantId && l.IsDeleted))
                .Should().Be(3);
        }

        using (var db = _fixture.CreateContext())
        {
            var repo = new EfUserDashboardRepository(db);
            var dashboard = (await repo.GetAsync(tenantId, userId, Key))!;
            dashboard.Widgets.Add(NewWidget(tenantId, removedKey));
            (await repo.SaveWithVersionAsync(dashboard, 2)).Should().BeTrue();
        }

        using (var db = _fixture.CreateContext())
        {
            var reloaded = (await new EfUserDashboardRepository(db).GetAsync(tenantId, userId, Key))!;
            reloaded.LayoutVersion.Should().Be(3);
            reloaded.Widgets.Should().HaveCount(total);
            reloaded.Widgets.Single(w => w.WidgetKey == removedKey).Layouts.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task SaveWithVersionAsync_ResetStyleRowReuse_RemovesStaleOverwritesRectsAndAddsMissingDefault()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var defaults = DashboardSeeder.BuildDefaultWidgets(tenantId, Key);
        var missingDefaultKey = defaults[^1].WidgetKey;

        // Customised state: a default widget missing, a non-default widget present, a default rect moved.
        using (var db = _fixture.CreateContext())
        {
            var custom = new UserDashboard
            {
                TenantId = tenantId, UserId = userId, DashboardKey = Key,
                Widgets = defaults.Take(defaults.Count - 1).ToList(),
            };
            custom.Widgets.Add(NewWidget(tenantId, "legacy-widget"));
            custom.Widgets[0].Layouts.Single(l => l.Breakpoint == Breakpoints.Desktop).Y = 40;
            (await new EfUserDashboardRepository(db).TryAddAsync(custom)).Should().BeTrue();
        }

        var expected = DashboardSeeder.BuildDefaultWidgets(tenantId, Key);
        Guid reusedWidgetId;
        using (var db = _fixture.CreateContext())
        {
            var repo = new EfUserDashboardRepository(db);
            var dashboard = (await repo.GetAsync(tenantId, userId, Key))!;
            reusedWidgetId = dashboard.Widgets.Single(w => w.WidgetKey == expected[0].WidgetKey).Id;
            var defaultKeys = expected.Select(d => d.WidgetKey).ToHashSet();

            foreach (var stale in dashboard.Widgets.Where(w => !defaultKeys.Contains(w.WidgetKey)).ToList())
                DashboardLayoutSupport.RemoveWidget(dashboard, stale);

            foreach (var def in expected)
            {
                var existing = dashboard.Widgets.FirstOrDefault(w => w.WidgetKey == def.WidgetKey);
                if (existing is null)
                {
                    def.Id = Guid.NewGuid();
                    def.DashboardId = dashboard.Id;
                    foreach (var l in def.Layouts) { l.Id = Guid.NewGuid(); l.WidgetId = def.Id; }
                    dashboard.Widgets.Add(def);
                    continue;
                }
                foreach (var l in def.Layouts)
                    DashboardLayoutSupport.SetRect(existing, l.Breakpoint, new GridRect(l.X, l.Y, l.W, l.H), tenantId);
            }

            (await repo.SaveWithVersionAsync(dashboard, 1)).Should().BeTrue();
        }

        using (var db = _fixture.CreateContext())
        {
            var reloaded = (await new EfUserDashboardRepository(db).GetAsync(tenantId, userId, Key))!;
            reloaded.LayoutVersion.Should().Be(2);
            reloaded.Widgets.Select(w => w.WidgetKey).Should().BeEquivalentTo(expected.Select(e => e.WidgetKey));
            reloaded.Widgets.Should().NotContain(w => w.WidgetKey == "legacy-widget");
            reloaded.Widgets.Should().Contain(w => w.WidgetKey == missingDefaultKey);
            reloaded.Widgets.Single(w => w.WidgetKey == expected[0].WidgetKey).Id.Should().Be(reusedWidgetId);
            foreach (var def in expected)
            {
                var stored = reloaded.Widgets.Single(w => w.WidgetKey == def.WidgetKey);
                stored.Layouts.Should().HaveCount(3);
                foreach (var l in def.Layouts)
                    DashboardLayoutSupport.Rect(stored, l.Breakpoint).Should().Be(new GridRect(l.X, l.Y, l.W, l.H));
            }
        }
    }

    [Fact]
    public async Task SaveWithVersionAsync_TwoContextsAtSameVersion_ExactlyOneWins()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedAsync(tenantId, userId);

        using var dbA = _fixture.CreateContext();
        using var dbB = _fixture.CreateContext();
        var repoA = new EfUserDashboardRepository(dbA);
        var repoB = new EfUserDashboardRepository(dbB);
        var a = (await repoA.GetAsync(tenantId, userId, Key))!;
        var b = (await repoB.GetAsync(tenantId, userId, Key))!;
        a.LayoutVersion.Should().Be(1);
        b.LayoutVersion.Should().Be(1);

        a.Widgets[0].Layouts.First().Y += 20;
        b.Widgets[0].Layouts.First().Y += 30;

        var resultA = await repoA.SaveWithVersionAsync(a, 1);
        var resultB = await repoB.SaveWithVersionAsync(b, 1);

        new[] { resultA, resultB }.Count(r => r).Should().Be(1);
        resultA.Should().BeTrue();
        resultB.Should().BeFalse();

        using var verify = _fixture.CreateContext();
        (await verify.UserDashboards.AsNoTracking().SingleAsync(d => d.TenantId == tenantId && d.UserId == userId))
            .LayoutVersion.Should().Be(2);

        // The loser's tracker was cleared, so a re-read in the same context returns the fresh state.
        var fresh = (await repoB.GetAsync(tenantId, userId, Key))!;
        fresh.LayoutVersion.Should().Be(2);
        fresh.Widgets[0].Layouts.Should().Contain(l => l.Y == a.Widgets[0].Layouts.First().Y);
    }

    [Fact]
    public async Task GetByWidgetIdAsync_FindsOwningDashboard_AndNullForOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var seeded = await SeedAsync(tenantId, userId);
        var widgetId = seeded.Widgets[0].Id;

        using var db = _fixture.CreateContext();
        var repo = new EfUserDashboardRepository(db);

        var found = await repo.GetByWidgetIdAsync(tenantId, widgetId);
        found.Should().NotBeNull();
        found!.Id.Should().Be(seeded.Id);
        found.Widgets.Should().Contain(w => w.Id == widgetId);

        using var otherDb = _fixture.CreateContext();
        (await new EfUserDashboardRepository(otherDb).GetByWidgetIdAsync(Guid.NewGuid(), widgetId)).Should().BeNull();
    }
}
