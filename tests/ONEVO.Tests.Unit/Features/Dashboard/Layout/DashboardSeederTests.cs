using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Dashboard;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class DashboardSeederTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static (DashboardSeeder Sut, Mock<IUserDashboardRepository> Repo) Create()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.TenantId).Returns(TenantId);
        user.SetupGet(u => u.UserId).Returns(UserId);
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        var repo = new Mock<IUserDashboardRepository>();
        return (new DashboardSeeder(repo.Object, user.Object), repo);
    }

    [Fact]
    public async Task Seeds_full_default_with_tenant_on_every_row_and_all_breakpoints()
    {
        var (sut, repo) = Create();
        repo.Setup(r => r.GetAsync(TenantId, UserId, DashboardKeys.MyWork, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDashboard?)null);
        UserDashboard? added = null;
        repo.Setup(r => r.TryAddAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>()))
            .Callback<UserDashboard, CancellationToken>((d, _) => added = d).ReturnsAsync(true);

        var result = await sut.EnsureAsync(DashboardKeys.MyWork, default);

        Assert.Same(added, result);
        Assert.Equal(TenantId, result.TenantId);
        Assert.Equal(UserId, result.UserId);
        Assert.Equal(DashboardKeys.MyWork, result.DashboardKey);
        Assert.Equal(DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyWork, Breakpoints.Desktop).Count, result.Widgets.Count);
        Assert.All(result.Widgets, w =>
        {
            Assert.Equal(TenantId, w.TenantId);
            Assert.Equal(3, w.Layouts.Count);
            Assert.All(w.Layouts, l => Assert.Equal(TenantId, l.TenantId));
            Assert.Equal(Breakpoints.All.OrderBy(b => b), w.Layouts.Select(l => l.Breakpoint).OrderBy(b => b));
        });
    }

    [Fact]
    public async Task Existing_dashboard_is_returned_without_insert()
    {
        var (sut, repo) = Create();
        var existing = new UserDashboard { UserId = UserId, DashboardKey = DashboardKeys.MyTeam };
        repo.Setup(r => r.GetAsync(TenantId, UserId, DashboardKeys.MyTeam, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.EnsureAsync(DashboardKeys.MyTeam, default);

        Assert.Same(existing, result);
        repo.Verify(r => r.TryAddAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Lost_race_rereads_and_returns_the_winner()
    {
        var (sut, repo) = Create();
        var winner = new UserDashboard { UserId = UserId, DashboardKey = DashboardKeys.MyWork };
        repo.SetupSequence(r => r.GetAsync(TenantId, UserId, DashboardKeys.MyWork, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDashboard?)null)
            .ReturnsAsync(winner);
        repo.Setup(r => r.TryAddAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await sut.EnsureAsync(DashboardKeys.MyWork, default);

        Assert.Same(winner, result);
    }

    [Fact]
    public void Repository_propagates_dashboard_tenant_to_children_with_empty_tenant()
    {
        var d = new UserDashboard { TenantId = TenantId };
        var w = new DashboardWidget();
        w.Layouts.Add(new DashboardWidgetLayout());
        d.Widgets.Add(w);

        EfUserDashboardRepository.PrepareGraph(d);

        Assert.Equal(TenantId, w.TenantId);
        Assert.Equal(TenantId, w.Layouts[0].TenantId);
        Assert.NotEqual(Guid.Empty, w.Id);
        Assert.NotEqual(Guid.Empty, w.Layouts[0].Id);
    }
}
