using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

/// <summary>Shared mocks and seeding for the dashboard mutation handler tests.</summary>
public sealed class DashboardMutationFixture
{
    public static readonly Guid TenantId = Guid.NewGuid();
    public static readonly Guid UserId = Guid.NewGuid();

    public static readonly string[] PlainEmployeeWork =
        DashboardWidgetRegistry.ForDashboard(DashboardKeys.MyWork).Select(d => d.Key).Where(k => k != "action-center").ToArray();

    public Mock<IUserDashboardRepository> Repo { get; } = new();
    public Mock<IDashboardWidgetAccess> Access { get; } = new();
    public Mock<ICurrentUser> User { get; } = new();

    public DashboardMutationFixture()
    {
        User.SetupGet(u => u.TenantId).Returns(TenantId);
        User.SetupGet(u => u.UserId).Returns(UserId);
        User.SetupGet(u => u.IsAuthenticated).Returns(true);
    }

    public UserDashboard Seed(string key, long version = 1, Guid? owner = null, IEnumerable<string>? allowed = null)
    {
        var d = new UserDashboard
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = owner ?? UserId, DashboardKey = key, LayoutVersion = version,
            Widgets = DashboardSeeder.BuildDefaultWidgets(TenantId, key),
        };
        foreach (var w in d.Widgets)
        {
            w.Id = Guid.NewGuid();
            w.DashboardId = d.Id;
            foreach (var l in w.Layouts) { l.Id = Guid.NewGuid(); l.WidgetId = w.Id; }
        }

        Repo.Setup(r => r.GetByIdAsync(TenantId, d.Id, It.IsAny<CancellationToken>())).ReturnsAsync(d);
        Repo.Setup(r => r.SaveWithVersionAsync(d, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDashboard dd, long v, CancellationToken _) =>
            {
                if (dd.LayoutVersion != v) return false;
                dd.LayoutVersion = v + 1;
                return true;
            });
        Allow(key, allowed ?? (key == DashboardKeys.MyWork ? PlainEmployeeWork : DashboardWidgetRegistry.ForDashboard(key).Select(x => x.Key)));
        return d;
    }

    public void Allow(string dashboardKey, IEnumerable<string> keys) =>
        Access.Setup(a => a.GetAllowedKeysAsync(dashboardKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(keys));

    public static GridRect Rect(DashboardWidget w, string breakpoint)
    {
        var l = w.Layouts.Single(x => x.Breakpoint == breakpoint && !x.IsDeleted);
        return new GridRect(l.X, l.Y, l.W, l.H);
    }
}
