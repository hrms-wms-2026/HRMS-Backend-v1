using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Queries.GetMyDashboards;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Layout;

public sealed class GetMyDashboardsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Mock<ICurrentUser> User(bool authenticated)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.TenantId).Returns(TenantId);
        user.SetupGet(u => u.UserId).Returns(UserId);
        user.SetupGet(u => u.IsAuthenticated).Returns(authenticated);
        return user;
    }

    [Fact]
    public async Task First_call_seeds_both_dashboards_with_full_default_including_action_center_for_plain_employee()
    {
        var user = User(true);
        var repo = new Mock<IUserDashboardRepository>();
        var store = new Dictionary<string, UserDashboard>();
        repo.Setup(r => r.GetAsync(TenantId, UserId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, string key, CancellationToken _) => store.GetValueOrDefault(key));
        repo.Setup(r => r.TryAddAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>()))
            .Callback<UserDashboard, CancellationToken>((d, _) => { d.Id = Guid.NewGuid(); store[d.DashboardKey] = d; })
            .ReturnsAsync(true);
        var handler = new GetMyDashboardsQueryHandler(new DashboardSeeder(repo.Object, user.Object), user.Object);

        var result = await handler.Handle(new GetMyDashboardsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(["my-work", "my-team"], result.Value!.Select(d => d.Key));
        Assert.Equal(["My Work", "My Team"], result.Value!.Select(d => d.Title));
        Assert.All(result.Value!, d => Assert.Equal(1, d.LayoutVersion));
        // Full default is stored regardless of permissions (plain employee still gets action-center).
        Assert.Contains(store[DashboardKeys.MyWork].Widgets, w => w.WidgetKey == "action-center");
        Assert.Equal(
            DashboardWidgetRegistry.DefaultLayout(DashboardKeys.MyTeam, Breakpoints.Desktop).Count,
            store[DashboardKeys.MyTeam].Widgets.Count);
    }

    [Fact]
    public async Task Seeding_race_returns_existing_dashboard()
    {
        var user = User(true);
        var winners = new Dictionary<string, UserDashboard>
        {
            [DashboardKeys.MyWork] = new() { Id = Guid.NewGuid(), DashboardKey = DashboardKeys.MyWork, LayoutVersion = 4 },
            [DashboardKeys.MyTeam] = new() { Id = Guid.NewGuid(), DashboardKey = DashboardKeys.MyTeam, LayoutVersion = 2 },
        };
        var reads = new Dictionary<string, int>();
        var repo = new Mock<IUserDashboardRepository>();
        repo.Setup(r => r.GetAsync(TenantId, UserId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, string key, CancellationToken _) =>
            {
                reads[key] = reads.GetValueOrDefault(key) + 1;
                return reads[key] == 1 ? null : winners[key];
            });
        repo.Setup(r => r.TryAddAsync(It.IsAny<UserDashboard>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new GetMyDashboardsQueryHandler(new DashboardSeeder(repo.Object, user.Object), user.Object);

        var result = await handler.Handle(new GetMyDashboardsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([winners[DashboardKeys.MyWork].Id, winners[DashboardKeys.MyTeam].Id], result.Value!.Select(d => d.Id));
        Assert.Equal([4L, 2L], result.Value!.Select(d => d.LayoutVersion));
    }

    [Fact]
    public async Task Unauthenticated_is_forbidden()
    {
        var user = User(false);
        var repo = new Mock<IUserDashboardRepository>();
        var handler = new GetMyDashboardsQueryHandler(new DashboardSeeder(repo.Object, user.Object), user.Object);

        var result = await handler.Handle(new GetMyDashboardsQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        repo.VerifyNoOtherCalls();
    }
}
